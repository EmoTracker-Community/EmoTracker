using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using EmoTracker.Data;
using System.Linq;

namespace EmoTracker.Extensions.VariantSwitcher
{
    public partial class VariantSwitcherControl : UserControl
    {
        public VariantSwitcherControl()
        {
            InitializeComponent();
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is not Control control || control.ContextMenu is not ContextMenu menu)
                return;

            var props = e.GetCurrentPoint(control).Properties;
            if (!props.IsLeftButtonPressed && !props.IsRightButtonPressed)
                return;

            PopulateVariantsMenu(menu);
            menu.PlacementTarget = control;
            menu.Open(control);
            e.Handled = true;
        }

        private static void PopulateVariantsMenu(ContextMenu menu)
        {
            menu.Items.Clear();

            var variants = Tracker.Instance.ActiveGamePackage?.AvailableVariants?.ToList();
            if (variants == null || variants.Count == 0)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = "No variants available",
                    IsEnabled = false,
                });
                return;
            }

            var command = EmoTracker.ApplicationModel.Instance.ActivatePackCommand;
            var activeVariant = Tracker.Instance.ActiveGamePackage?.ActiveVariant;
            foreach (var variant in variants)
            {
                menu.Items.Add(new MenuItem
                {
                    Header = BuildVariantHeader(variant, variant == activeVariant),
                    Command = command,
                    CommandParameter = variant,
                });
            }
        }

        private static object BuildVariantHeader(IGamePackageVariant variant, bool isActive)
        {
            var panel = new Grid();
            panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            panel.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(20, GridUnitType.Pixel)) { MinWidth = 20 });
            panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

            var name = new TextBlock
            {
                Text = variant.DisplayName,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(name, 0);
            panel.Children.Add(name);

            if (isActive)
            {
                var activeBadge = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1.5),
                    BorderBrush = Brushes.Gray,
                    Margin = new Thickness(10, 5, 0, 5),
                    Child = new TextBlock
                    {
                        Text = "Active",
                        FontSize = 10,
                        FontWeight = FontWeight.Bold,
                        Margin = new Thickness(2),
                    },
                };
                Grid.SetColumn(activeBadge, 2);
                panel.Children.Add(activeBadge);
            }

            return panel;
        }
    }
}
