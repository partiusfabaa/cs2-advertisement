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
    || !css.Contains("max-width: 90%;") || !css.Contains("padding: 10px 18px;")
    || !css.Contains("font-size: 24px;") || css.Contains("font-size: 36px;"))
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

foreach (var count in new[] { 0, 1, 8, 32 })
foreach (var duration in new[] { 0d, 1d, 1.2d, 320d })
{
    var states = new long[count];
    var sends = 0;
    for (var tick = 0; tick < Math.Ceiling(duration) + 1; tick++)
        for (var slot = 0; slot < count; slot++)
            if (HtmlPlayback.Advance(ref states[slot], duration, false, false) == HtmlDecision.Print) sends++;
    if (sends != count * (int)Math.Ceiling(duration)) throw new Exception("HTML send count changed");
}
long elapsed = 0;
for (var tick = 0; tick < 1000; tick++)
    if (HtmlPlayback.Advance(ref elapsed, 2, true, true) != HtmlDecision.Paused || elapsed != 0)
        throw new Exception("Alive ticks consumed budget");
if (HtmlPlayback.Advance(ref elapsed, 2, true, false) != HtmlDecision.Print) throw new Exception();
if (HtmlPlayback.Advance(ref elapsed, 2, true, true) != HtmlDecision.Paused || elapsed != 1) throw new Exception();
if (HtmlPlayback.Advance(ref elapsed, 2, true, false) != HtmlDecision.Print) throw new Exception();
if (HtmlPlayback.Advance(ref elapsed, 2, true, false) != HtmlDecision.Expired) throw new Exception();
elapsed = 0; // replacement message resets its own display budget
if (HtmlPlayback.Advance(ref elapsed, 1, false, true) != HtmlDecision.Print) throw new Exception();
Console.WriteLine("PASS: HTML 0/1/8/32 recipients, zero/fractional duration, alive/dead pause and replacement budget.");

var rotation = new Advertisement.Advertisement { Messages = [new() { Chat = "A" }, new() { Chat = "B" }, new() { Chat = "C" }] };
for (var i = 0; i < 100000; i++)
    if (rotation.NextMessage.Chat != new[] { "A", "B", "C" }[i % 3]) throw new Exception("Rotation order changed");
var single = new Advertisement.Advertisement { Messages = [new() { Chat = "Only" }] };
for (var i = 0; i < 100000; i++)
    if (single.NextMessage.Chat != "Only") throw new Exception("Single rotation changed");
Console.WriteLine("PASS: rotation wrap and single-message groups.");
