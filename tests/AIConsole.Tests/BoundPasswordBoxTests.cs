using System.Windows.Controls;
using System.Windows.Data;
using AIConsole.ViewModels;
using AIConsole.Views.Controls;

namespace AIConsole.Tests;

public class BoundPasswordBoxTests
{
    [Fact]
    public void TypingIntoInitiallyEmptyKey_UpdatesTheBoundValue()
    {
        TestThread.RunSta(() =>
        {
            var endpoint = new EndpointVm { Id = "second", Name = "Second", ApiKey = "" };
            var box = new BoundPasswordBox();
            BindingOperations.SetBinding(box, BoundPasswordBox.BoundPasswordProperty,
                new Binding(nameof(EndpointVm.ApiKey))
                {
                    Source = endpoint,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                });

            var inner = Assert.IsType<PasswordBox>(box.FindName("Inner"));
            Assert.Equal("", inner.Password);

            inner.Password = "sk-second";

            Assert.Equal("sk-second", endpoint.ApiKey);
            Assert.Equal("sk-second", box.BoundPassword);
        });
    }

    [Fact]
    public void BoundValue_IsPushedIntoThePasswordBox()
    {
        TestThread.RunSta(() =>
        {
            var box = new BoundPasswordBox();
            var inner = Assert.IsType<PasswordBox>(box.FindName("Inner"));

            box.BoundPassword = "preset-key";

            Assert.Equal("preset-key", inner.Password);
        });
    }
}
