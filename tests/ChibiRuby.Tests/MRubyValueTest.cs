// using MRuby;
//
// namespace ChibiRuby.Tests;
//
// public class MRubyValueTest
// {
//     [Test]
//     public void FixnumValue()
//     {
//         var x = MRubyValue.From(123);
//         GC.Collect();
//         Assert.Multiple(() =>
//         {
//             Assert.That(x.IsInteger, Is.True);
//             Assert.That(x.VType, Is.EqualTo(MRubyVType.Integer));
//             Assert.That(x.FixnumValue, Is.EqualTo(123));
//         });
//     }
//
//     [Test]
//     public void FloatValue()
//     {
//         var x = MRubyValue.From(123.45678);
//         GC.Collect();
//         Assert.Multiple(() =>
//         {
//             Assert.That(x.IsFloat, Is.True);
//             Assert.That(x.VType, Is.EqualTo(MRubyVType.Float));
//             Assert.That(x.FloatValue, Is.EqualTo(123.45678).Within(0.0001));
//         });
//
//     }
//
//     [Test]
//     public void SymbolValue()
//     {
//         var sym = new Symbol(123);
//         var x = MRubyValue.From(sym);
//         GC.Collect();
//
//         Assert.Multiple(() =>
//         {
//             Assert.That(x.IsSymbol, Is.True);
//             Assert.That(x.VType, Is.EqualTo(MRubyVType.Symbol));
//             Assert.That(x.SymbolValue.Value, Is.EqualTo(123));
//         });
//     }
// }
