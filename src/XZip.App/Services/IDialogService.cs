using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Resources;

namespace XZip.App.Services;

public interface IDialogService
{
    Task ShowMessageAsync(string title, string message, XamlRoot xamlRoot);
    Task<bool> ConfirmAsync(string title, string message, XamlRoot xamlRoot,
        string primaryText = "OK", string closeText = "Cancel");
}

public sealed class DialogService : IDialogService
{
    private readonly ResourceLoader _resources = ResourceLoader.GetForViewIndependentUse();

    public Task ShowMessageAsync(string title, string message, XamlRoot xamlRoot)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = T("Dialog_Ok"),
            DefaultButton = ContentDialogButton.Close,
        };
        return dlg.ShowAsync().AsTask();
    }

    public async Task<bool> ConfirmAsync(string title, string message, XamlRoot xamlRoot,
        string primaryText = "", string closeText = "")
    {
        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = string.IsNullOrWhiteSpace(primaryText) ? T("Dialog_Ok") : primaryText,
            CloseButtonText = string.IsNullOrWhiteSpace(closeText) ? T("Dialog_Cancel") : closeText,
            DefaultButton = ContentDialogButton.Primary,
        };
        var res = await dlg.ShowAsync();
        return res == ContentDialogResult.Primary;
    }

    private string T(string key)
    {
        var value = _resources.GetString(key);
        return string.IsNullOrWhiteSpace(value) ? key : value;
    }
}
