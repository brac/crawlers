using Crawlers.Generation;
using Xunit;
using Xunit.Abstractions;

namespace Crawlers.Tests.Generation;

public class AsciiPreviewTest
{
    private readonly ITestOutputHelper _output;
    public AsciiPreviewTest(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Visual: render a floor for manual inspection (always passes)")]
    public void Render_sample_floor()
    {
        var floor = new BspFloorGenerator().Generate(new GenerationConfig
        {
            Width = 60,
            Height = 30,
            Seed = 12345
        });
        _output.WriteLine($"Seed: {floor.Seed}, Rooms: {floor.Rooms.Count}");
        _output.WriteLine(FloorAsciiRenderer.Render(floor));
    }
}
