namespace HstReceipts.Core.Models;

/// <summary>Summary of correction learning after Export Excel and save.</summary>
public sealed class AiLearningResult
{
    public bool Ran { get; set; }
    public string Message { get; set; } = string.Empty;
    public int GroupsProcessed { get; set; }
    public int ProfilesUpdated { get; set; }
    public int FieldsLearned { get; set; }
}
