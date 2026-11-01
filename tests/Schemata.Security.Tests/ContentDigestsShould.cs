using System.Text;
using Schemata.Security.Foundation.Signatures;
using Xunit;

namespace Schemata.Security.Tests;

public class ContentDigestsShould
{
    private static readonly byte[] Body = Encoding.ASCII.GetBytes("{\"hello\": \"world\"}");

    [Fact]
    public void Compute_The_Sha512_Digest_Of_The_Appendix_B_Test_Body() {
        Assert.Equal(
            "sha-512=:WZDPaVn/7XgHaAy8pmojAkGWoRx2UFChF41A2svX+TaPm+AbwAgBWnrIiYllu7BNNyealdVLvRwEmTHWXvJwew==:",
            ContentDigests.Sha512(Body)
        );
    }

    [Fact]
    public void Compute_The_Sha256_Digest_Of_The_Appendix_B_Test_Body() {
        Assert.Equal("sha-256=:X48E9qOokqqrvdts8nOJRJN3OWDUoyWxBf7kbu9DBPE=:", ContentDigests.Sha256(Body));
    }
}
