using MaarifPlatform.Application.Visuals;

namespace MaarifPlatform.Tests.Visuals;

public class MathExpressionEvaluatorTests
{
    [Theory]
    [InlineData("2*x-3", 5, 7)]
    [InlineData("x+1", 4, 5)]
    [InlineData("x/2", 10, 5)]
    [InlineData("2+3*4", 0, 14)] // çarpma toplamadan önce
    [InlineData("(2+3)*4", 0, 20)]
    [InlineData("x^2", 3, 9)]
    [InlineData("-x^2", 3, -9)] // birli işaret üsten GEVŞEK bağlanır: -(3^2), (-3)^2 DEĞİL
    [InlineData("x^-1", 4, 0.25)] // üs de işaretli olabilir: 4^(-1) = 1/4
    [InlineData("2^3^2", 0, 512)] // sağdan-birleşimli: 2^(3^2) = 2^9, (2^3)^2 DEĞİL
    [InlineData("sqrt(x)", 16, 4)]
    [InlineData("abs(x)", -7, 7)]
    [InlineData("pi", 0, Math.PI)]
    [InlineData("e", 0, Math.E)]
    [InlineData("2*pi*x", 1, 2 * Math.PI)]
    public void Evaluate_ValidExpression_ReturnsExpectedValue(string expression, double x, double expected)
    {
        var result = MathExpressionEvaluator.Evaluate(expression, x);
        Assert.Equal(expected, result, precision: 10);
    }

    [Fact]
    public void Evaluate_SinCosAtKnownAngles_ReturnsExpectedValue()
    {
        Assert.Equal(0, MathExpressionEvaluator.Evaluate("sin(x)", 0), precision: 10);
        Assert.Equal(1, MathExpressionEvaluator.Evaluate("cos(x)", 0), precision: 10);
    }

    [Fact]
    public void Compile_CalledOnce_CanBeEvaluatedManyTimesWithDifferentX()
    {
        var f = MathExpressionEvaluator.Compile("2*x+1");
        Assert.Equal(1, f(0));
        Assert.Equal(3, f(1));
        Assert.Equal(21, f(10));
    }

    [Fact]
    public void Evaluate_DivisionByZero_ReturnsInfinityNotException()
    {
        var result = MathExpressionEvaluator.Evaluate("1/x", 0);
        Assert.True(double.IsInfinity(result));
    }

    [Fact]
    public void Evaluate_SqrtOfNegative_ReturnsNaNNotException()
    {
        var result = MathExpressionEvaluator.Evaluate("sqrt(x)", -4);
        Assert.True(double.IsNaN(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("2*x+")]
    [InlineData("2*(x+1")]
    [InlineData("2*x+1)")]
    [InlineData("2 $ x")]
    [InlineData("notafunction(x)")]
    [InlineData("y+1")] // yalnızca x değişkeni tanınır
    [InlineData("2*x 3")] // sayıdan sonra beklenmeyen fazladan token
    public void Evaluate_InvalidExpression_ThrowsFormatException(string expression)
    {
        Assert.Throws<FormatException>(() => MathExpressionEvaluator.Evaluate(expression, 1));
    }
}
