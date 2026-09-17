using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AccC3DMetadata.Config;
using AccC3DMetadata.Models;

namespace AccC3DMetadata.UI
{
    /// <summary>
    /// Two-step wizard for creating or editing the <c>accsync.xml</c> mapping config for the
    /// open drawing: pick a block, then choose which of its attributes to sync and how.
    /// </summary>
    public class ConfigEditorDialog : Window
    {
        private readonly List<(string BlockName, List<string> AttributeTags)> _blocks;
        private readonly SyncConfig _existingConfig;
        private string _selectedBlock;
        private ObservableCollection<ConfigEditorRow> _rows;

        private readonly Grid _pickerPage;
        private readonly Grid _tablePage;
        private DataGrid _grid;
        private TextBlock _tableSubtitle;

        private static readonly SolidColorBrush AccBlue = Freeze(
            new SolidColorBrush(Color.FromRgb(0, 120, 212))
        );
        private static readonly SolidColorBrush TextDark = Freeze(
            new SolidColorBrush(Color.FromRgb(50, 49, 48))
        );
        private static readonly SolidColorBrush TextMuted = Freeze(
            new SolidColorBrush(Color.FromRgb(96, 94, 92))
        );
        private static readonly SolidColorBrush BorderGray = Freeze(
            new SolidColorBrush(Color.FromRgb(200, 198, 196))
        );
        private static readonly SolidColorBrush FooterBg = Freeze(
            new SolidColorBrush(Color.FromRgb(243, 242, 241))
        );
        private static readonly SolidColorBrush RowAlt = Freeze(
            new SolidColorBrush(Color.FromRgb(248, 247, 246))
        );

        /// <summary>The merged config to save, populated when the user clicks Save.</summary>
        public SyncConfig Result { get; private set; }

        public ConfigEditorDialog(
            List<(string BlockName, List<string> AttributeTags)> blocks,
            SyncConfig existingConfig
        )
        {
            _blocks = blocks;
            _existingConfig = existingConfig;

            Title = "ACC Sync — Config Editor";
            Width = 820;
            MinHeight = 420;
            MaxHeight = 700;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            SizeToContent = SizeToContent.Height;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) });
            root.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            );

            var header = new Border { Background = AccBlue, Padding = new Thickness(16, 0, 16, 0) };
            header.Child = new TextBlock
            {
                Text = "Autodesk Forma Attribute Mapping",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(header, 0);

            _pickerPage = BuildBlockPickerPage();
            _tablePage = BuildAttributeTablePage();
            _tablePage.Visibility = Visibility.Collapsed;

            Grid.SetRow(_pickerPage, 1);
            Grid.SetRow(_tablePage, 1);

            root.Children.Add(header);
            root.Children.Add(_pickerPage);
            root.Children.Add(_tablePage);
            Content = root;
        }

        // ── Page 1: block picker ────────────────────────────────────────────────────

        private Grid BuildBlockPickerPage()
        {
            var page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            );
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var instruction = new TextBlock
            {
                Text = "Select the block whose attributes you want to sync with Autodesk Forma.",
                Foreground = TextMuted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16, 12, 16, 6),
            };
            Grid.SetRow(instruction, 0);

            var list = new StackPanel { Margin = new Thickness(16, 0, 16, 8) };
            var scroller = new ScrollViewer
            {
                Content = list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            Grid.SetRow(scroller, 1);

            string preselect = _existingConfig
                ?.Mappings.FirstOrDefault(m => m.Target == MappingTarget.BlockAttribute)
                ?.BlockName;

            RadioButton firstButton = null;
            foreach (var (blockName, tags) in _blocks)
            {
                var rb = new RadioButton
                {
                    GroupName = "BlockPicker",
                    Content =
                        $"{blockName}  ({tags.Count} attribute{(tags.Count == 1 ? "" : "s")})",
                    Tag = blockName,
                    Margin = new Thickness(0, 4, 0, 4),
                    FontSize = 12,
                    Foreground = TextDark,
                };
                rb.Checked += (_, _) => _selectedBlock = (string)rb.Tag;
                firstButton ??= rb;

                if (
                    preselect != null
                    && string.Equals(blockName, preselect, StringComparison.OrdinalIgnoreCase)
                )
                    rb.IsChecked = true;

                list.Children.Add(rb);
            }
            if (
                list.Children.OfType<RadioButton>().All(r => r.IsChecked != true)
                && firstButton != null
            )
                firstButton.IsChecked = true;

            var footer = BuildFooter(
                ("Cancel", false, (_, _) => DialogResult = false),
                ("Next", true, (_, _) => GoToAttributeTable())
            );
            Grid.SetRow(footer, 2);

            page.Children.Add(instruction);
            page.Children.Add(scroller);
            page.Children.Add(footer);
            return page;
        }

        // ── Page 2: attribute table ─────────────────────────────────────────────────

        private Grid BuildAttributeTablePage()
        {
            var page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            );
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _tableSubtitle = new TextBlock
            {
                Foreground = TextMuted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(16, 12, 16, 6),
            };
            Grid.SetRow(_tableSubtitle, 0);

            _grid = new DataGrid
            {
                Margin = new Thickness(16, 4, 16, 4),
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = BorderGray,
                AlternatingRowBackground = RowAlt,
                RowBackground = Brushes.White,
                BorderBrush = BorderGray,
                BorderThickness = new Thickness(1),
                ColumnHeaderHeight = 32,
                RowHeight = 30,
            };

            _grid.Columns.Add(
                new DataGridCheckBoxColumn
                {
                    Header = "Use",
                    Binding = new Binding("Use") { Mode = BindingMode.TwoWay },
                    Width = new DataGridLength(48),
                }
            );
            _grid.Columns.Add(
                new DataGridTextColumn
                {
                    Header = "Attribute Name",
                    Binding = new Binding("AttributeName"),
                    IsReadOnly = true,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                }
            );
            _grid.Columns.Add(
                new DataGridTextColumn
                {
                    Header = "Forma Name",
                    Binding = new Binding("FormaName")
                    {
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                    },
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                }
            );
            _grid.Columns.Add(
                new DataGridComboBoxColumn
                {
                    Header = "Direction",
                    SelectedItemBinding = new Binding("Direction") { Mode = BindingMode.TwoWay },
                    ItemsSource = new[]
                    {
                        SyncDirection.ReadWrite,
                        SyncDirection.Read,
                        SyncDirection.Write,
                    },
                    Width = new DataGridLength(120),
                }
            );
            _grid.Columns.Add(
                new DataGridComboBoxColumn
                {
                    Header = "Conflict Strategy",
                    SelectedItemBinding = new Binding("ConflictStrategy")
                    {
                        Mode = BindingMode.TwoWay,
                    },
                    ItemsSource = new[]
                    {
                        ConflictStrategy.Prompt,
                        ConflictStrategy.AccWins,
                        ConflictStrategy.DwgWins,
                        ConflictStrategy.Skip,
                    },
                    Width = new DataGridLength(140),
                }
            );

            Grid.SetRow(_grid, 1);

            var footer = BuildFooter(
                ("Back", false, (_, _) => GoToBlockPicker()),
                ("Cancel", false, (_, _) => DialogResult = false),
                ("Save", true, (_, _) => OnSave())
            );
            Grid.SetRow(footer, 2);

            page.Children.Add(_tableSubtitle);
            page.Children.Add(_grid);
            page.Children.Add(footer);
            return page;
        }

        // ── Navigation ───────────────────────────────────────────────────────────────

        private void GoToAttributeTable()
        {
            if (_selectedBlock == null)
                return;

            var tags = _blocks.First(b => b.BlockName == _selectedBlock).AttributeTags;
            var existingByTag = _existingConfig
                ?.Mappings.Where(m =>
                    m.Target == MappingTarget.BlockAttribute
                    && string.Equals(
                        m.BlockName,
                        _selectedBlock,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToDictionary(m => m.BlockAttributeTag, StringComparer.OrdinalIgnoreCase);

            var rows = new ObservableCollection<ConfigEditorRow>();
            foreach (string tag in tags)
            {
                if (existingByTag != null && existingByTag.TryGetValue(tag, out var existing))
                {
                    rows.Add(
                        new ConfigEditorRow
                        {
                            AttributeName = tag,
                            Use = true,
                            FormaName = existing.AccAttributeName,
                            Direction = existing.Direction,
                            ConflictStrategy = existing.ConflictStrategy,
                        }
                    );
                }
                else
                {
                    rows.Add(
                        new ConfigEditorRow
                        {
                            AttributeName = tag,
                            // Only default new attributes to "used" when there's no config yet to edit against.
                            Use = _existingConfig == null,
                            FormaName = tag,
                        }
                    );
                }
            }

            _rows = rows;
            _grid.ItemsSource = _rows;
            _tableSubtitle.Text =
                $"Block: {_selectedBlock} — tick the attributes to sync, then Save.";

            _pickerPage.Visibility = Visibility.Collapsed;
            _tablePage.Visibility = Visibility.Visible;
        }

        private void GoToBlockPicker()
        {
            _tablePage.Visibility = Visibility.Collapsed;
            _pickerPage.Visibility = Visibility.Visible;
        }

        private void OnSave()
        {
            var newBlockMappings = _rows
                .Where(r => r.Use)
                .Select(r => new SyncMapping
                {
                    Target = MappingTarget.BlockAttribute,
                    BlockName = _selectedBlock,
                    BlockAttributeTag = r.AttributeName,
                    AccAttributeName = string.IsNullOrWhiteSpace(r.FormaName)
                        ? r.AttributeName
                        : r.FormaName.Trim(),
                    Direction = r.Direction,
                    ConflictStrategy = r.ConflictStrategy,
                })
                .ToList();

            // Keep any mappings that belong to a different block or a property set untouched —
            // this editor session only replaces the mappings for the block just edited.
            var preserved =
                _existingConfig
                    ?.Mappings.Where(m =>
                        !(
                            m.Target == MappingTarget.BlockAttribute
                            && string.Equals(
                                m.BlockName,
                                _selectedBlock,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                    )
                    .ToList()
                ?? new List<SyncMapping>();

            Result = new SyncConfig
            {
                HubId = _existingConfig?.HubId,
                ProjectId = _existingConfig?.ProjectId,
                DrawingItemId = _existingConfig?.DrawingItemId,
                Mappings = preserved.Concat(newBlockMappings).ToList(),
            };

            DialogResult = true;
        }

        // ── Shared UI helpers ────────────────────────────────────────────────────────

        private Border BuildFooter(
            params (string Text, bool IsDefault, RoutedEventHandler Handler)[] buttons
        )
        {
            var footer = new Border
            {
                Background = FooterBg,
                BorderBrush = BorderGray,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(16, 10, 16, 10),
            };
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            foreach (var (text, isDefault, handler) in buttons)
            {
                var btn = new Button
                {
                    Content = text,
                    Width = 90,
                    Height = 28,
                    Margin = new Thickness(8, 0, 0, 0),
                    IsDefault = isDefault,
                };
                btn.Click += handler;
                panel.Children.Add(btn);
            }
            footer.Child = panel;
            return footer;
        }

        private static T Freeze<T>(T freezable)
            where T : System.Windows.Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
