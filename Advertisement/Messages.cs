using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Advertisement;

public class Advertisement
{
    public float Interval { get; init; }
    public List<AdMessage> Messages { get; set; } = [];
    private int _currentMessageIndex;
    [JsonIgnore] public AdMessage NextMessage
    {
        get
        {
            var message = Messages[_currentMessageIndex];
            _currentMessageIndex = (_currentMessageIndex + 1) % Messages.Count;
            return message;
        }
    }
}

public class AdMessage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Chat { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Center { get; init; }
}

public class AdCommand : AdMessage
{
    [JsonPropertyOrder(-1)]
    public List<string> Names { get; set; } = [];
}