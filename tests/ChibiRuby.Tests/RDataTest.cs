namespace ChibiRuby.Tests;

[TestFixture]
public class RDataTest
{
    [Test]
    public void Instantiate()
    {
        var userdata = new Dictionary<string, int>
        {
            { "hoge", 123 },
            { "fuga", 456 }
        };

        var data = new RData(userdata);
        using var state = MRubyState.Create();

        state.SetConst(state.Intern("MYDATA"u8), state.ObjectClass, data);

        state.DefineMethod(state.ObjectClass, state.Intern("do_something"u8), (s, self) =>
        {
            var dataValue = s.GetConst(state.Intern("MYDATA"u8), state.ObjectClass);
            var dataFromRuby = dataValue.As<RData>();
            var userdataFromRuby = data.Data as Dictionary<string, int>;
            Assert.That(userdata, Is.Not.Null);
            Assert.That(userdata.Count, Is.EqualTo(2));
            Assert.That(userdata["hoge"], Is.EqualTo(123));

            userdataFromRuby!["hoge"] = 999;
            return default;
        });

        state.Send(state.ObjectClass, state.Intern("do_something"u8));

        Assert.That(userdata["hoge"], Is.EqualTo(999));
    }
}