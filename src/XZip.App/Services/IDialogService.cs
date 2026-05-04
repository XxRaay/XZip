using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace XZip.App.Services;

public interface IDialogService
{
    Task ShowMessageAsync(string title, string message, XamlRoot xamlRoot);
    Task<bool> ConfirmAsync(string title, string message, XamlRoot xamlRoot,
        string primaryText = "OK", string closeText = "Cancel");
}

public sealed class DialogService : IDialogService
{
    public Task ShowMessageAsync(string title, string message, XamlRoot xamlRoot)
    {
        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
        };
        return dlg.ShowAsync().AsTask();
    }

    public async Task<bool> ConfirmAsync(string title, string message, XamlRoot xamlRoot,
        string primaryText = "OK", string closeText = "Cancel")
    {
        var dlg = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Primary,
        };
        var res = await dlg.ShowAsync();
        return res == ContentDialogResult.Primary;
    }
}
