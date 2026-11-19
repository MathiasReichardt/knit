using Knit.Core.Discovery;
using Knit.Data;

namespace Knit.Cli.Tests;

public class IdShortenerTests
{
    [Fact]
    public void Each_id_gets_its_shortest_unique_prefix_with_a_floor_of_four()
    {
        string[] ids =
        [
            "ABCDEFGHJK00000000", // shares 7 chars with the next
            "ABCDEFGXXX00000000",
            "ABCDZZZZZZ00000000", // shares 4 with both above
            "QRST00000000000000", // shares 4 with the next only
            "QRSTU0000000000000",
            "Z00000000000000000", // no neighbor shares anything
        ];
        var root = Directory.CreateTempSubdirectory("knit-shortener-").FullName;
        try
        {
            var itemsDir = RepoDiscovery.ItemsDir(root);
            Directory.CreateDirectory(itemsDir);
            foreach (var id in ids) File.WriteAllText(Path.Combine(itemsDir, $"{id}-x.md"), "");

            var shortener = IdShortener.FromItemsDir(root);

            Assert.Equal(
                ["abcdefgh", "abcdefgx", "abcdz", "qrst0", "qrstu", "z000"],
                ids.Select(shortener.Short).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
