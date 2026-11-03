using TeamsJarvis.Core;

namespace TeamsJarvis.Tests;

public class IcsExporterTests
{
    private static Reminder Sample(string text) =>
        new(42, text, new DateTimeOffset(2026, 7, 15, 9, 30, 0, TimeSpan.Zero), "Fabric", false, false);

    [Fact]
    public void ProducesValidVEvent()
    {
        var ics = IcsExporter.Build(Sample("Revisar el pipeline"));
        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("END:VEVENT", ics);
        Assert.Contains("END:VCALENDAR", ics);
        Assert.Contains("UID:jarvis-reminder-42@teamsjarvis", ics);
        Assert.Contains("SUMMARY:Revisar el pipeline", ics);
    }

    [Fact]
    public void UsesUtcTimestampsAndDuration()
    {
        var ics = IcsExporter.Build(Sample("x"), durationMinutes: 30);
        Assert.Contains("DTSTART:20260715T093000Z", ics);
        Assert.Contains("DTEND:20260715T100000Z", ics);
    }

    [Fact]
    public void IncludesAlarmAndCategory()
    {
        var ics = IcsExporter.Build(Sample("x"));
        Assert.Contains("BEGIN:VALARM", ics);
        Assert.Contains("TRIGGER:-PT10M", ics);
        Assert.Contains("CATEGORIES:Fabric", ics);
    }

    [Fact]
    public void EscapesSpecialCharacters()
    {
        var ics = IcsExporter.Build(Sample("Llamar a Ana; luego, revisar"));
        Assert.Contains(@"SUMMARY:Llamar a Ana\; luego\, revisar", ics);
    }

    [Fact]
    public void UsesCrlfLineEndings()
    {
        var ics = IcsExporter.Build(Sample("x"));
        Assert.Contains("\r\n", ics);
    }
}
