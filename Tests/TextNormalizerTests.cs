using BackendRequisicionPersonal.Helpers;
using Xunit;

namespace BackendRequisicionPersonal.Tests
{
    public class TextNormalizerTests
    {
        [Theory]
        [InlineData("EN NOMINA", "EN NOMINA")]
        [InlineData(" en  nomina ", "EN NOMINA")]
        [InlineData("EN N”MINA", "EN NOMINA")]
        [InlineData("En NÛMina", "EN NOMINA")]
        [InlineData(null, "")]
        [InlineData("   ", "")]
        public void NormalizeForComparison_NormalizesAccentsCaseAndTrim(string? input, string expected)
        {
            var actual = TextNormalizer.NormalizeForComparison(input);
            Assert.Equal(expected, actual);
        }
    }
}
