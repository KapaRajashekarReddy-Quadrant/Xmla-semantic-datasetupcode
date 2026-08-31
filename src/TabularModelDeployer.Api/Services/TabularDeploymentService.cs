// using Microsoft.AnalysisServices.Tabular;
// using TabularModelDeployer.Api.Models;
// using System.Linq;

// namespace TabularModelDeployer.Api.Services;

// public class TabularDeploymentService
// {
//     private readonly IConfiguration _config;

//     public TabularDeploymentService(IConfiguration config)
//     {
//         _config = config;
//     }

//     // Maps the "type" string coming from the incoming JSON payload
//     // (string, int64, double, boolean, dateTime, date) to a Tabular DataType.
//     private static DataType MapColumnType(string type) => type.ToLower() switch
//     {
//         "double" => DataType.Double,
//         "int64" => DataType.Int64,
//         "boolean" => DataType.Boolean,
//         "datetime" => DataType.DateTime,
//         "date" => DataType.DateTime,
//         _ => DataType.String
//     };

//     public string DeployModel(DeploymentRequest request)
//     {
//         // 🔒 Service Principal from appsettings.json
//         var tenantId = _config["PowerBI:TenantId"];
//         var clientId = _config["PowerBI:ClientId"];
//         var clientSecret = _config["PowerBI:ClientSecret"];

//         // 🔄 Dynamic values from endpoint
//         var workspaceUrl = $"powerbi://api.powerbi.com/v1.0/myorg/{request.WorkspaceName}";
//         var lakehouseServer = request.LakehouseServer;
//         var lakehouseDb = request.LakehouseDatabase;

//         string conn = $"Provider=MSOLAP;Data Source={workspaceUrl};" +
//                       $"User ID=app:{clientId}@{tenantId};Password={clientSecret};";

//         var server = new Server();
//         server.Connect(conn);

//         var schema = request.ModelSchema;
//         var db = server.Databases.FindByName(schema.Model_Name);

//         // ---------------- DATABASE CREATION ----------------
//         if (db == null)
//         {
//             db = new Database
//             {
//                 Name = schema.Model_Name,
//                 ID = schema.Model_Name,
//                 CompatibilityLevel = 1500
//             };
//             db.Model = new Model
//             {
//                 Name = schema.Model_Name
//             };
//             server.Databases.Add(db);
//         }

//         var model = db.Model;

//         // IMPORTANT: Do NOT clear tables/relationships every time unless doing full rebuild
//         // model.Tables.Clear();
//         // model.Relationships.Clear();
//         // model.SaveChanges();

//         // ---------------- TABLE CREATION / UPDATE ----------------
//         foreach (var t in schema.Tables)
//         {
//             var table = model.Tables.Find(t.Name);
//             bool tableWasNew = false;

//             if (table == null)
//             {
//                 table = new Table { Name = t.Name };
//                 tableWasNew = true;
//                 model.Tables.Add(table);
//             }

//             bool isMeasureOnlyTable =
//                 (t.Columns == null || t.Columns.Count == 0) &&
//                 (t.Measures != null && t.Measures.Count > 0);

//             // 1️⃣ Add / Update Columns
//             if (t.Columns != null)
//             {
//                 foreach (var c in t.Columns)
//                 {
//                     var existing = table.Columns.Find(c.Name) as DataColumn;

//                     if (existing != null)
//                     {
//                         // Update existing column
//                         existing.DataType = MapColumnType(c.Type);
//                         existing.SourceColumn = c.Name;
//                         if (c.IsHidden == true) existing.IsHidden = true;
//                     }
//                     else
//                     {
//                         var column = new DataColumn
//                         {
//                             Name = c.Name,
//                             SourceColumn = c.Name,
//                             DataType = MapColumnType(c.Type)
//                         };
//                         if (c.IsHidden == true) column.IsHidden = true;
//                         table.Columns.Add(column);
//                     }
//                 }
//             }

//             // 🔥 If measure-only table AND no visible columns exist → add dummy
//             if (isMeasureOnlyTable && !table.Columns.Any(col => !col.IsHidden))
//             {
//                 if (table.Columns.Find("DummyColumn") == null)
//                 {
//                     var dummy = new DataColumn
//                     {
//                         Name = "DummyColumn",
//                         DataType = DataType.String,
//                         IsHidden = true
//                     };
//                     table.Columns.Add(dummy);
//                 }
//             }

//             // 2️⃣ Create / Update Partition (simplified – overwrites if exists)
//             var partition = table.Partitions.Find("MainPartition");
//             if (partition == null)
//             {
//                 partition = new Partition { Name = "MainPartition" };
//                 table.Partitions.Add(partition);
//             }

//             partition.Mode = t.Is_Physical ? ModeType.DirectQuery : ModeType.Import;

//             var msource = new MPartitionSource();
//             if (t.Is_Physical)
//             {
//                 msource.Expression = $@"
// let
//     Source = Sql.Database(""{lakehouseServer}"", ""{lakehouseDb}""),
//     Nav = Source{{[Schema=""dbo"",Item=""{t.Name}""]}}[Data]
// in
//     Nav";
//             }
//             else
//             {
//                 msource.Expression = @"
// let
//     Source = #table(
//         {""ORDER_AMOUNT""},
//         {
//             {100},
//             {200},
//             {300}
//         }
//     )
// in
//     Source";
//             }
//             partition.Source = msource;

//             // 3️⃣ Add / Update Measures
//             if (t.Measures != null)
//             {
//                 foreach (var m in t.Measures)
//                 {
//                     var existingMeasure = table.Measures.Find(m.Name);

//                     if (existingMeasure != null)
//                     {
//                         // Update existing measure
//                         existingMeasure.Expression = m.Expression;
//                         if (!string.IsNullOrWhiteSpace(m.DefaultFormat))
//                             existingMeasure.FormatString = m.DefaultFormat;
//                     }
//                     else
//                     {
//                         var measure = new Measure
//                         {
//                             Name = m.Name,
//                             Expression = m.Expression
//                         };

//                         if (!string.IsNullOrWhiteSpace(m.DefaultFormat))
//                             measure.FormatString = m.DefaultFormat;

//                         table.Measures.Add(measure);
//                     }
//                 }
//             }
//         }

//         // ---------------- RELATIONSHIPS ----------------
//         foreach (var r in schema.Relationships)
//         {
//             var fromTable = model.Tables.Find(r.From_Table);
//             var toTable = model.Tables.Find(r.To_Table);

//             if (fromTable == null || toTable == null) continue;

//             var fromColumn = fromTable.Columns.Find(r.From_Col) as DataColumn;
//             var toColumn = toTable.Columns.Find(r.To_Col) as DataColumn;

//             if (fromColumn == null || toColumn == null) continue;

//             var existingRel = model.Relationships
//                 .OfType<SingleColumnRelationship>()
//                 .FirstOrDefault(rel =>
//                     rel.FromColumn == fromColumn &&
//                     rel.ToColumn == toColumn);

//             if (existingRel == null)
//             {
//                 var relationship = new SingleColumnRelationship
//                 {
//                     Name = r.Name ?? $"{fromColumn.Name}_to_{toColumn.Name}",
//                     FromColumn = fromColumn,
//                     ToColumn = toColumn
//                     // IsActive = true, CrossFilteringBehavior = etc. if needed
//                 };
//                 model.Relationships.Add(relationship);
//             }
//             // else → could update properties if needed
//         }

//         // 🔥 SAVE (with dummy columns if needed)
//         model.SaveChanges();

//         // 🔥 REMOVE DUMMY COLUMNS FROM MEASURE-ONLY TABLES
//         bool needsFinalSave = false;
//         foreach (var table in model.Tables)
//         {
//             var dummy = table.Columns.Find("DummyColumn");
//             if (dummy != null && table.Measures.Any())
//             {
//                 table.Columns.Remove(dummy);
//                 needsFinalSave = true;
//             }
//         }

//         if (needsFinalSave)
//         {
//             model.SaveChanges();
//         }

//         server.Disconnect();

//         return "🔥 Model deployed/updated successfully!";
//     }
// }


using Microsoft.AnalysisServices.Tabular;
using TabularModelDeployer.Api.Models;
using System.Linq;
using System.Text.Json;

namespace TabularModelDeployer.Api.Services;

public class TabularDeploymentService
{
    private readonly IConfiguration _config;

    public TabularDeploymentService(IConfiguration config)
    {
        _config = config;
    }

    public string DeployModel(DeploymentRequest request)
    {
        // 🔒 Service Principal from appsettings.json
        var tenantId = _config["PowerBI:TenantId"];
        var clientId = _config["PowerBI:ClientId"];
        var clientSecret = _config["PowerBI:ClientSecret"];

        // 🔄 Dynamic values from endpoint
        var workspaceUrl = $"powerbi://api.powerbi.com/v1.0/myorg/{request.WorkspaceName}";
        var lakehouseServer = request.LakehouseServer;
        var lakehouseDb = request.LakehouseDatabase;

        string conn = $"Provider=MSOLAP;Data Source={workspaceUrl};" +
                      $"User ID=app:{clientId}@{tenantId};Password={clientSecret};";

        var server = new Server();
        server.Connect(conn);

        var schema = request.ModelSchema;
        var db = server.Databases.FindByName(schema.Model_Name);

        // ---------------- DATABASE CREATION ----------------
        if (db == null)
        {
            db = new Database
            {
                Name = schema.Model_Name,
                ID = schema.Model_Name,
                CompatibilityLevel = 1500
            };
            db.Model = new Model
            {
                Name = schema.Model_Name
            };
            server.Databases.Add(db);
        }

        var model = db.Model;

        // ---------------- TABLE CREATION / UPDATE ----------------
        foreach (var t in schema.Tables)
        {
            var table = model.Tables.Find(t.Name);

            if (table == null)
            {
                table = new Table { Name = t.Name };
                model.Tables.Add(table);
            }

            bool isMeasureOnlyTable = 
                (t.Columns == null || t.Columns.Count == 0) &&
                (t.Measures != null && t.Measures.Count > 0);

            // 1️⃣ Add / Update Columns
            if (t.Columns != null)
            {
                foreach (var c in t.Columns)
                {
                    var existing = table.Columns.Find(c.Name) as DataColumn;

                    if (existing != null)
                    {
                        existing.DataType = c.Type.ToLower() switch
                        {
                            "double" => DataType.Double,
                            "date" => DataType.DateTime,
                            _ => DataType.String
                        };
                        existing.SourceColumn = c.Name;
                        if (c.IsHidden == true) existing.IsHidden = true;
                    }
                    else
                    {
                        var column = new DataColumn
                        {
                            Name = c.Name,
                            SourceColumn = c.Name,
                            DataType = c.Type.ToLower() switch
                            {
                                "double" => DataType.Double,
                                "date" => DataType.DateTime,
                                _ => DataType.String
                            }
                        };
                        if (c.IsHidden == true) column.IsHidden = true;
                        table.Columns.Add(column);
                    }
                }
            }

            // 🔥 If measure-only table AND no visible columns exist → add dummy
            if (isMeasureOnlyTable && !table.Columns.Any(col => !col.IsHidden))
            {
                if (table.Columns.Find("DummyColumn") == null)
                {
                    var dummy = new DataColumn
                    {
                        Name = "DummyColumn",
                        DataType = DataType.String,
                        IsHidden = true
                    };
                    table.Columns.Add(dummy);
                }
            }

            // 2️⃣ Create / Update Partition
            var partition = table.Partitions.Find("MainPartition");
            if (partition == null)
            {
                partition = new Partition { Name = "MainPartition" };
                table.Partitions.Add(partition);
            }

            partition.Mode = t.Is_Physical ? ModeType.DirectQuery : ModeType.Import;

            var msource = new MPartitionSource();
            if (t.Is_Physical)
            {
                msource.Expression = $@"
let
    Source = Sql.Database(""{lakehouseServer}"", ""{lakehouseDb}""),
    Nav = Source{{[Schema=""dbo"",Item=""{t.Name}""]}}[Data]
in
    Nav";
            }
            else
            {
                msource.Expression = @"
let
    Source = #table(
        {""ORDER_AMOUNT""},
        {
            {100},
            {200},
            {300}
        }
    )
in
    Source";
            }
            partition.Source = msource;

            // 3️⃣ Add / Update Measures
            if (t.Measures != null)
            {
                foreach (var m in t.Measures)
                {
                    var existingMeasure = table.Measures.Find(m.Name);

                    if (existingMeasure != null)
                    {
                        existingMeasure.Expression = m.Expression;
                    }
                    else
                    {
                        var measure = new Measure
                        {
                            Name = m.Name,
                            Expression = m.Expression
                        };
                        table.Measures.Add(measure);
                    }
                }
            }
        }

        // ---------------- RELATIONSHIPS ----------------
        foreach (var r in schema.Relationships)
        {
            var fromTable = model.Tables.Find(r.From_Table);
            var toTable = model.Tables.Find(r.To_Table);

            if (fromTable == null || toTable == null) continue;

            var fromColumn = fromTable.Columns.Find(r.From_Col) as DataColumn;
            var toColumn = toTable.Columns.Find(r.To_Col) as DataColumn;

            if (fromColumn == null || toColumn == null) continue;

            var existingRel = model.Relationships
                .OfType<SingleColumnRelationship>()
                .FirstOrDefault(rel => 
                    rel.FromColumn == fromColumn && 
                    rel.ToColumn == toColumn);

            if (existingRel == null)
            {
                var relationship = new SingleColumnRelationship
                {
                    Name = r.Name ?? $"{fromColumn.Name}_to_{toColumn.Name}",
                    FromColumn = fromColumn,
                    ToColumn = toColumn
                };
                model.Relationships.Add(relationship);
            }
        }

        // ---------------- APPLY CUSTOM DIAGRAM LAYOUT ----------------
        ApplyCustomDiagramLayout(model);

        // 🔥 SAVE (with dummy columns if needed)
        model.SaveChanges();

        // 🔥 REMOVE DUMMY COLUMNS FROM MEASURE-ONLY TABLES
        bool needsFinalSave = false;
        foreach (var table in model.Tables)
        {
            var dummy = table.Columns.Find("DummyColumn");
            if (dummy != null && table.Measures.Any())
            {
                table.Columns.Remove(dummy);
                needsFinalSave = true;
            }
        }

        if (needsFinalSave)
        {
            model.SaveChanges();
        }

        server.Disconnect();

        return "🔥 Model deployed/updated successfully with Custom Layout View!";
    }

    private void ApplyCustomDiagramLayout(Model model)
    {
        // Filter out 'Measures1' or any table name starting with 'Measure'
        var schemaTables = model.Tables
            .Where(t => !string.Equals(t.Name, "Measures1", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var nodes = schemaTables.Select((table, index) => new
        {
            location = new
            {
                x = (index % 3) * 320 + 50,
                y = (index / 3) * 260 + 50
            },
            node = new
            {
                name = table.Name,
                type = 0
            },
            size = new
            {
                height = 200,
                width = 220
            },
            zIndex = index + 1
        }).ToList();

        var layoutStructure = new
        {
            version = "1.1.0",
            diagrams = new[]
            {
                new
                {
                    name = "Model Layout",
                    ordinal = 0,
                    scrollPosition = new { x = 0, y = 0 },
                    zoomValue = 100,
                    nodes = nodes
                }
            }
        };

        string serializedLayout = JsonSerializer.Serialize(layoutStructure);

        if (model.Annotations.Contains("__PBI_DiagramLayout"))
        {
            model.Annotations["__PBI_DiagramLayout"].Value = serializedLayout;
        }
        else
        {
            model.Annotations.Add(new Annotation
            {
                Name = "__PBI_DiagramLayout",
                Value = serializedLayout
            });
        }
    }
}
