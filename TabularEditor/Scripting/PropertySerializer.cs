﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TabularEditor.TOMWrapper;

namespace TabularEditor.Scripting
{
    public static class PropertySerializer
    {
        private static string GetTsvForObject(TabularObject obj, string properties)
        {
            var props = properties.Split(',');
            var sb = new StringBuilder();
            sb.Append(obj.GetObjectPath());
            foreach (var prop in props)
            {
                sb.Append('\t');
                var pInfo = obj.GetType().GetProperty(prop);
                if (pInfo != null)
                {
                    var pValue = pInfo.GetValue(obj);
                    if (pValue == null)
                        continue;
                    else if (pValue is TabularObject)
                        // Improve GetObjectPath to always provide unique path, and create corresponding method to resolve a path
                        sb.Append((pValue as TabularObject).GetObjectPath());
                    else
                        sb.Append(pValue.ToString().Replace("\n", "\\n").Replace("\t", "\\t"));
                }
            }
            return sb.ToString();
        }

        [ScriptMethod]
        // TODO: Provide more formatting options
        public static string ExportProperties(this IEnumerable<ITabularNamedObject> objects, string properties = "Name,Description,SourceColumn,Expression,FormatString,DataType")
        {
            var sb = new StringBuilder();
            sb.Append("Object\t");
            sb.Append(properties.Replace(",", "\t"));
            foreach (var obj in objects.OfType<TabularObject>())
            {
                // Only certain types of objects can have their properties exported:
                // TODO: Change this to a HashSet lookup
                switch(obj.ObjectType)
                {
                    case ObjectType.Table:
                    case ObjectType.Partition:
                    case ObjectType.DataSource:
                    case ObjectType.Expression:
                    case ObjectType.Column:
                    case ObjectType.Role:
                    case ObjectType.Model:
                    case ObjectType.Hierarchy:
                    case ObjectType.Level:
                    case ObjectType.Measure:
                    case ObjectType.KPI:
                    case ObjectType.Relationship:
                    case ObjectType.Perspective:
                        break;
                    default:
                        continue;
                }

                sb.Append("\n");
                sb.Append(GetTsvForObject(obj, properties));
            }
            return sb.ToString();
        }

        [ScriptMethod]
        public static void ImportProperties(string tsvData)
        {
            var rows = tsvData.Split('\n');
            var properties = string.Join(",", rows[0].Replace("\r","").Split('\t').Skip(1).ToArray());
            foreach (var row in rows.Skip(1))
            {
                if (!string.IsNullOrWhiteSpace(row))
                    AssignTsvToObject(row, properties);
            }
        }

        private static void AssignTsvToObject(string propertyValues, string properties)
        {
            var props = properties.Split(',');
            var values = propertyValues.Replace("\r","").Split('\t').Select(v => v.Replace("\\n", "\n").Replace("\\t", "\t")).ToArray();
            var obj = ResolveObjectPath(values[0]);
            if (obj == null) return;

            for (int i = 0; i < props.Length; i++) {
                var pInfo = obj.GetType().GetProperty(props[i]);

                // Consider only properties that exist, and have a public setter:
                if (pInfo == null || !pInfo.CanWrite || !pInfo.GetSetMethod(true).IsPublic) continue;

                var pValue = values[i + 1]; // This is shifted by 1 since the first column is the Object path
                if (typeof(TabularObject).IsAssignableFrom(pInfo.PropertyType))
                {
                    // Object references need to be resolved:
                    var pValueObj = ResolveObjectPath(pValue);
                    pInfo.SetValue(obj, pValueObj);
                }
                else if (pInfo.PropertyType.IsEnum)
                {
                    // Value is conerted from string to an enum type:
                    pInfo.SetValue(obj, Enum.Parse(pInfo.PropertyType, pValue));
                }
                else {
                    // Value is converted directly from string to the type of the property:
                    pInfo.SetValue(obj, Convert.ChangeType(pValue, pInfo.PropertyType));
                }
            }
        }

        [ScriptMethod]
        public static string GetObjectPath(this TabularObject obj)
        {
            return TabularObjectHelper.GetObjectPath(obj);
        }

        [ScriptMethod]
        public static TabularObject ResolveObjectPath(string path)
        {
            var parts = path.Split('.');

            var partsFixed = new List<string>();

            // Objects that have "." in their name, will be enclosed by square brackets. So let's traverse the array
            // and concatenate any parts between a set of square brackets:
            string partFraction = null;
            foreach (var p in parts)
            {
                if(partFraction == null)
                {
                    if (p.StartsWith("["))
                    {
                        if (p.EndsWith("]"))
                        {
                            partFraction = p.Substring(1, p.Length - 2);
                            partsFixed.Add(partFraction.ToLower());
                            partFraction = null;
                        }
                        else
                            partFraction = p.Substring(1);
                    }
                    else
                        partsFixed.Add(p.ToLower());
                } else
                {
                    if (p.EndsWith("]"))
                    {
                        partFraction += "." + p.Substring(0, p.Length - 1);
                        partsFixed.Add(partFraction.ToLower());
                        partFraction = null;
                    }
                    else
                        partFraction += "." + p;
                }
            }
            parts = partsFixed.ToArray();
            
            var model = TabularModelHandler.Singleton.Model;
            if (model == null || parts.Length == 0) return null;
            if (parts.Length == 1 && parts[0] == "model") return model;
            switch(parts[0])
            {
                case "model":
                    var table = model.Tables.FirstOrDefault(x => x.Name.EqualsI(parts[1]));
                    if (parts.Length == 2 || table == null) return table;
                    var obj = table.GetChildren().OfType<TabularNamedObject>().FirstOrDefault(c => c.Name.EqualsI(parts[2]));
                    if (parts.Length == 3 || obj == null) return obj;
                    if (obj is Hierarchy && parts.Length == 4) return (obj as Hierarchy).Levels.FirstOrDefault(x => x.Name.EqualsI(parts[3]));
                    if (obj is Measure && parts[3] == "kpi" && parts.Length == 4) return (obj as Measure).KPI;
                    if (obj is Column && parts[3] == "variations" && parts.Length == 5) return (obj as Column).Variations.FirstOrDefault(x => x.Name.EqualsI(parts[4]));
                    return null;

                case "relationship": return model.Relationships.FirstOrDefault(x => x.Name.EqualsI(parts[1]));
                case "datasource": return model.DataSources.FirstOrDefault(x => x.Name.EqualsI(parts[1]));
                case "role": return model.Roles.FirstOrDefault(x => x.Name.EqualsI(parts[1]));
                case "expression": return model.Expressions.FirstOrDefault(x => x.Name.EqualsI(parts[1]));
                case "perspective": return model.Perspectives.FirstOrDefault(x => x.Name.EqualsI(parts[1]));
                case "culture": return model.Cultures.FirstOrDefault(x => x.Name.EqualsI(parts[1]));
                case "tablepartition":
                    if (parts.Length != 3) return null;
                    table = model.Tables.FirstOrDefault(x => x.Name.EqualsI(parts[1]));
                    if (table == null) return null;
                    return table.Partitions.FirstOrDefault(x => x.Name.EqualsI(parts[2]));

                default:
                    // "Reseller Sales.Sales Amount" is equivalent to "Model.Reseller Sales.Sales Amount":
                    table = model.Tables.FirstOrDefault(x => x.Name.EqualsI(parts[0]));
                    if (parts.Length == 1 || table == null) return null;
                    obj = table.GetChildren().OfType<TabularNamedObject>().FirstOrDefault(c => c.Name.EqualsI(parts[1]));
                    if (parts.Length == 2 || obj == null) return obj;
                    if (obj is Hierarchy && parts.Length == 3) return (obj as Hierarchy).Levels.FirstOrDefault(x => x.Name.EqualsI(parts[2]));
                    if (obj is Measure && parts[2] == "kpi" && parts.Length == 3) return (obj as Measure).KPI;
                    if (obj is Column && parts[2] == "variations" && parts.Length == 4) return (obj as Column).Variations.FirstOrDefault(x => x.Name.EqualsI(parts[3]));
                    return null;
            }
        }
    }
}
