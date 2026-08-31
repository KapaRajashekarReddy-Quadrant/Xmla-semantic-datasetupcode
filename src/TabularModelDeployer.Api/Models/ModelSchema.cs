namespace TabularModelDeployer.Api.Models;

public class DeploymentRequest
{
    public string WorkspaceName { get; set; } = string.Empty;
    public string LakehouseServer { get; set; } = string.Empty;
    public string LakehouseDatabase { get; set; } = string.Empty;
    public ModelSchema ModelSchema { get; set; } = new();
}

public class ModelSchema
{
    public string Model_Name { get; set; } = string.Empty;
    public List<TableConfig> Tables { get; set; } = new();
    public List<RelationshipConfig> Relationships { get; set; } = new();
}

public class TableConfig
{
    public string Name { get; set; } = string.Empty;
    public bool Is_Physical { get; set; }
    public List<ColumnConfig> Columns { get; set; } = new();
    public List<MeasureConfig>? Measures { get; set; }
}

public class ColumnConfig
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool? IsHidden { get; set; }
}

public class MeasureConfig
{
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public string? DataType { get; set; }
    public string? Role { get; set; }
    public string? DefaultFormat { get; set; }
}

public class RelationshipConfig
{
    public string Name { get; set; } = string.Empty;
    public string From_Table { get; set; } = string.Empty;
    public string From_Col { get; set; } = string.Empty;
    public string To_Table { get; set; } = string.Empty;
    public string To_Col { get; set; } = string.Empty;
}



