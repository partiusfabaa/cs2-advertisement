using System.Xml.Linq;
using Advertisement;

CheckRuns("Plain text", new PanoramaTextRun("Plain text", "default"));
CheckRuns("{RED}Alert {GREEN}Ready {DEFAULT}Go",
    new("Alert ", "red"), new("Ready ", "green"), new("Go", "default"));
CheckRuns("{rEd}Hello", new PanoramaTextRun("Hello", "red"));
CheckRuns("{RED}{GREEN}Hello{RED}", new PanoramaTextRun("Hello", "green"));
CheckRuns("{RED}One{RED} two", new PanoramaTextRun("One two", "red"));
CheckRuns("{unknown} {MAP}", new PanoramaTextRun("{unknown} {MAP}", "default"));
CheckRuns("{BLUE} <font>Text</font> & text ", new PanoramaTextRun(" <font>Text</font> & text ", "blue"));
CheckRuns("");
CheckRuns("{RED}{DEFAULT}");

var root = args[0];
var panorama = Path.Combine(root, "Advertisement", "workshop", "content", "csgo_addons", "advertisement_panorama", "panorama");
var xml = XDocument.Load(Path.Combine(panorama, "layout", "custom_game", "advertisement_panorama.xml"));
var labels = xml.Descendants("Label").ToList();
if (labels.Count != PanoramaText.MaxRuns)
    throw new Exception("Label pool and parser limit differ.");
for (var index = 0; index < PanoramaText.MaxRuns; index++)
{
    if (!labels.Any(label => (string?)label.Attribute("id") == $"AdvertisementText{index}"
        && (string?)label.Attribute("text") == $"{{s:message{index}}}"))
        throw new Exception($"Missing label/variable for segment {index}.");
}

var css = File.ReadAllText(Path.Combine(panorama, "styles", "custom_game", "advertisement_panorama.css"));
if (!css.Contains("width: fit-children;") || !css.Contains("height: fit-children;")
    || !css.Contains("max-width: 90%;") || css.Contains("width: 880px;"))
    throw new Exception("Card must fit its contents rather than use the original fixed width.");
var colors = new[] { "DEFAULT", "WHITE", "DARKRED", "GREEN", "LIGHTYELLOW", "LIGHTBLUE", "OLIVE",
    "LIME", "RED", "LIGHTPURPLE", "PURPLE", "GREY", "YELLOW", "GOLD", "SILVER", "BLUE", "DARKBLUE",
    "BLUEGREY", "MAGENTA", "LIGHTRED", "ORANGE" };
foreach (var color in colors)
{
    var run = PanoramaText.Parse($"{{{color}}}sample").Single();
    if (!css.Contains($".message.color-{run.Color} "))
        throw new Exception($"Missing CSS color {color}.");
}

var overLimit = PanoramaText.Parse(string.Concat(Enumerable.Range(0, 17)
    .Select(index => index % 2 == 0 ? "{RED}a" : "{GREEN}b")));
if (overLimit.Count != 17 || string.Concat(overLimit.Select(run => run.Text)).Length != 17)
    throw new Exception("Parser lost text exceeding the label pool.");
Console.WriteLine("PASS: color parsing, whitespace, reset, casing, all colors, label contract, and overflow text preservation.");

static void CheckRuns(string message, params PanoramaTextRun[] expected)
{
    if (!PanoramaText.Parse(message).SequenceEqual(expected))
        throw new Exception($"Incorrect color runs for: {message}");
}
