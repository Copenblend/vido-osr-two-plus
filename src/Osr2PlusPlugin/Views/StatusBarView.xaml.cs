using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using Osr2PlusPlugin.ViewModels;

namespace Osr2PlusPlugin.Views;

/// <summary>
/// Status bar item showing connection status text with reactive color updates.
/// </summary>
public partial class StatusBarView : UserControl
{
    private SidebarViewModel? _viewModel;

    public StatusBarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel != null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = e.NewValue as SidebarViewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdateForeground();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SidebarViewModel.StatusTextColor))
            UpdateForeground();
    }

    private void UpdateForeground()
    {
        if (_viewModel == null) return;

        var color = (Color)ColorConverter.ConvertFromString(_viewModel.StatusTextColor);
        StatusTextBlock.Foreground = new SolidColorBrush(color);
    }
}
