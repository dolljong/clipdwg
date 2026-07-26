using System;
using System.Threading;
using ClipDwg.Style;
using ClipDwg.Ui;
using Xunit;

namespace ClipDwg.Tests;

/// <summary>
/// 손으로 짠 WinForms 레이아웃은 초기화 중에 터지는 것이 가장 흔한 고장이다.
/// 화면에 띄우지 않고 구성만 해 봐서 그걸 잡는다.
/// </summary>
public class OptionsFormTests
{
    [Fact]
    public void Construct_WithDefaultSettings_DoesNotThrow()
    {
        Exception? error = RunSta(() =>
        {
            using var form = new OptionsForm(SettingsFile.CreateDefault());
            Assert.Null(form.Result);
            Assert.Equal("clipdwg 옵션", form.Text);
        });

        Assert.Null(error);
    }

    [Fact]
    public void Construct_WithEmptyProfileList_CreatesOne()
    {
        Exception? error = RunSta(() =>
        {
            var settings = new SettingsFile();
            using var form = new OptionsForm(settings);
            Assert.NotEmpty(settings.Profiles);
        });

        Assert.Null(error);
    }

    [Fact]
    public void Construct_WithTrueColorRules_DoesNotThrow()
    {
        Exception? error = RunSta(() =>
        {
            SettingsFile settings = SettingsFile.CreateDefault();
            settings.GetActiveProfile().Widths.Add(new WidthRule { Rgb = "#FF8000", Mm = 0.6 });
            using var form = new OptionsForm(settings);
        });

        Assert.Null(error);
    }

    private static Exception? RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));
        return error;
    }
}
