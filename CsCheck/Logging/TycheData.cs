namespace CsCheck.Logging;

public record TycheData(
    string type, double run_start, string property, string status, string representation,
    string? status_reason, Dictionary<string, string> arguments, string? how_generated, Dictionary<string, string> features,
    Dictionary<string, string>? coverage, Dictionary<string, string> timing, Dictionary<string, string> metadata);