using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WarframeMarket_StandingToPlat
{
    public partial class MainWindow : Window
    {
        private List<(string Name, string ModsFile)> syndicates = new();
        private List<Order> currentOrders = new();
        private List<string> currentProcessedMods = new();
        private List<string> currentModsWithNoOrders = new();
        private string selectedSyndicateName = "";
        private string lastScanFilePath = "";

        public MainWindow()
        {
            InitializeComponent();
            LoadSyndicates();
        }

        private void AuthorizeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://api.warframe.market/v2/oauth/authorize",
                    UseShellExecute = true
                });
                StatusText.Text = "Opening OAuth authorization in your browser...";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to open OAuth page: {ex.Message}";
            }
        }

        private void LoadSyndicates()
        {
            try
            {
                syndicates = GetSyndicates();
                
                // If no syndicates found from files, add some test syndicates
                if (syndicates.Count == 0)
                {
                    syndicates = new List<(string Name, string ModsFile)>
                    {
                        ("Arbiters Of Hexis", "ArbitersOfHexis.txt"),
                        ("Cephalon Suda", "CephalonSuda.txt"),
                        ("New Loka", "NewLoka.txt"),
                        ("Red Veil", "RedVeil.txt"),
                        ("Steel Meridian", "SteelMeridian.txt"),
                        ("The Perrin Sequence", "ThePerrinSequence.txt")
                    };
                }
                
                var syndicateNames = syndicates.Select(s => s.Name).ToList();
                SyndicateComboBox.ItemsSource = syndicateNames;
                StatusText.Text = $"Loaded {syndicates.Count} syndicates: {string.Join(", ", syndicateNames)}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error loading syndicates: {ex.Message}";
            }
        }

        private List<(string Name, string ModsFile)> GetSyndicates()
        {
            var syndicateList = new List<(string Name, string ModsFile)>();
            
            // Use the project root directory where we know the WarframeSyndicateMods folder exists
            string projectRoot = Directory.GetCurrentDirectory();
            string modsFolderPath = Path.Combine(projectRoot, "WarframeSyndicateMods");

            if (!Directory.Exists(modsFolderPath))
            {
                return syndicateList;
            }

            string[] modFiles = Directory.GetFiles(modsFolderPath, "*.txt");

            foreach (var file in modFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string syndicateName = Regex.Replace(fileName, "(\\B[A-Z])", " $1");
                syndicateList.Add((syndicateName, file));
            }

            return syndicateList;
        }

        private void SyndicateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FetchButton.IsEnabled = SyndicateComboBox.SelectedIndex >= 0;
        }

        private async void FetchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SyndicateComboBox.SelectedIndex < 0) return;

            var selectedSyndicate = syndicates[SyndicateComboBox.SelectedIndex];
            selectedSyndicateName = selectedSyndicate.Name;

            FetchButton.IsEnabled = false;
            StatusText.Text = "Fetching market data...";

            try
            {
                var mods = File.ReadAllLines(selectedSyndicate.ModsFile);
                var (orders, processedMods, modsWithNoOrders) = await FetchOrders(mods);

                currentOrders = orders;
                currentProcessedMods = processedMods;
                currentModsWithNoOrders = modsWithNoOrders;

                // Save the scan data for later retrieval
                SaveLastScanData();

                DisplayResults();
                ExportButton.IsEnabled = true;
                StatusText.Text = $"Found {orders.Count} orders for {processedMods.Count} mods";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
                ResultsPanel.Children.Clear();
                ResultsPanel.Children.Add(new TextBlock
                {
                    Text = $"Error fetching data: {ex.Message}",
                    Foreground = Brushes.Red,
                    FontSize = 14,
                    Margin = new Thickness(10)
                });
            }
            finally
            {
                FetchButton.IsEnabled = true;
            }
        }

        private async Task<(List<Order> orders, List<string> processedMods, List<string> modsWithNoOrders)> FetchOrders(string[] mods)
        {
            var orders = new List<Order>();
            var processedMods = new List<string>();
            var modsWithNoOrders = new List<string>();

            using var client = new System.Net.Http.HttpClient();

            foreach (var mod in mods)
            {
                var url = $"https://api.warframe.market/v2/orders/item/{mod.Trim()}";
                
                try
                {
                    var response = await client.GetFromJsonAsync<WarframeMarketResponse>(url);

                    if (response?.Data != null && response.Data.Length > 0)
                    {
                        var sortedOrders = response.Data
                            .Where(order =>
                                order.Visible &&
                                order.Type == "sell" &&
                                order.User.Status == "ingame" &&
                                order.Rank == 0)
                            .OrderByDescending(order => order.Platinum)
                            .Take(5)
                            .Select(order =>
                            {
                                order.ItemId = mod.Trim();
                                order.ItemName = FormatItemName(mod);
                                return order;
                            })
                            .ToList();

                        if (sortedOrders.Count > 0)
                        {
                            orders.AddRange(sortedOrders);
                            processedMods.Add(mod.Trim());
                        }
                        else
                        {
                            modsWithNoOrders.Add(mod.Trim());
                        }
                    }
                    else
                    {
                        modsWithNoOrders.Add(mod.Trim());
                    }
                }
                catch
                {
                    modsWithNoOrders.Add(mod.Trim());
                }

                await Task.Delay(200);
            }

            return (orders, processedMods, modsWithNoOrders);
        }

        private void DisplayResults()
        {
            ResultsPanel.Children.Clear();

            if (currentOrders.Count > 0)
            {
                var groupedOrders = currentOrders.OrderBy(o => o.Platinum).GroupBy(o => o.ItemId).ToList();

                foreach (var group in groupedOrders)
                {
                    var firstOrder = group.First();
                    var marketUrl = GetWarframeMarketUrl(firstOrder.ItemId);

                    // Create mod header - split layout (left: mod info, right: post order)
                    var modHeader = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(0, 0, 0, 10),
                        Padding = new Thickness(15)
                    };

                    // Create main grid for split layout
                    var headerGrid = new Grid();
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Left side
                    headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Right side

                    // Left side - Mod name and link
                    var leftStack = new StackPanel();
                    leftStack.Children.Add(new TextBlock
                    {
                        Text = firstOrder.ItemName,
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White
                    });

                    var linkButton = new Button
                    {
                        Content = $"🔗 View on Warframe Market",
                        Background = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                        Foreground = Brushes.White,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 5, 0, 0),
                        Cursor = Cursors.Hand
                    };
                    linkButton.Click += (s, e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = marketUrl,
                        UseShellExecute = true
                    });

                    leftStack.Children.Add(linkButton);
                    Grid.SetColumn(leftStack, 0);
                    headerGrid.Children.Add(leftStack);

                    // Right side - Post Order fields in columns
                    var rightStack = new StackPanel();
                    
                    // Post Order header
                    rightStack.Children.Add(new TextBlock
                    {
                        Text = "📝 Post Your Order",
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        Margin = new Thickness(0, 0, 0, 8),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });

                    // Input fields in columns
                    var inputGrid = new Grid();
                    inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Label
                    inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); // Input
                    inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Label
                    inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); // Input
                    inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) }); // Label
                    inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) }); // Input
                    inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) }); // Button

                    // Quantity
                    var quantityLabel = new TextBlock
                    {
                        Text = "Qty:",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(0, 0, 3, 0)
                    };
                    Grid.SetColumn(quantityLabel, 0);
                    inputGrid.Children.Add(quantityLabel);

                    var quantityTextBox = new TextBox
                    {
                        Text = "1",
                        Height = 22,
                        FontSize = 10,
                        Background = new SolidColorBrush(Color.FromRgb(61, 61, 61)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        Padding = new Thickness(3, 2, 3, 2)
                    };
                    Grid.SetColumn(quantityTextBox, 1);
                    inputGrid.Children.Add(quantityTextBox);

                    // Cost
                    var costLabel = new TextBlock
                    {
                        Text = "Cost:",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(8, 0, 3, 0)
                    };
                    Grid.SetColumn(costLabel, 2);
                    inputGrid.Children.Add(costLabel);

                    var costTextBox = new TextBox
                    {
                        Text = group.OrderByDescending(o => o.Platinum).Last().Platinum.ToString(),
                        Height = 22,
                        FontSize = 10,
                        Background = new SolidColorBrush(Color.FromRgb(61, 61, 61)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        Padding = new Thickness(3, 2, 3, 2)
                    };
                    Grid.SetColumn(costTextBox, 3);
                    inputGrid.Children.Add(costTextBox);

                    // Rank
                    var rankLabel = new TextBlock
                    {
                        Text = "Rank:",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Margin = new Thickness(8, 0, 3, 0)
                    };
                    Grid.SetColumn(rankLabel, 4);
                    inputGrid.Children.Add(rankLabel);

                    var rankTextBox = new TextBox
                    {
                        Text = "0",
                        Height = 22,
                        FontSize = 10,
                        Background = new SolidColorBrush(Color.FromRgb(61, 61, 61)),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(100, 100, 100)),
                        Padding = new Thickness(3, 2, 3, 2)
                    };
                    Grid.SetColumn(rankTextBox, 5);
                    inputGrid.Children.Add(rankTextBox);

                    // Post Order button
                    var postOrderButton = new Button
                    {
                        Content = "📝 Post",
                        Height = 22,
                        FontSize = 10,
                        FontWeight = FontWeights.Bold,
                        Background = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                        Foreground = Brushes.White,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(8, 0, 0, 0),
                        Cursor = Cursors.Hand
                    };
                    Grid.SetColumn(postOrderButton, 6);
                    inputGrid.Children.Add(postOrderButton);

                    // Add click handler for the button
                    postOrderButton.Click += (s, e) => PostOrderInline(firstOrder.ItemId, firstOrder.ItemName, quantityTextBox.Text, costTextBox.Text, rankTextBox.Text);

                    rightStack.Children.Add(inputGrid);
                    Grid.SetColumn(rightStack, 1);
                    headerGrid.Children.Add(rightStack);

                    modHeader.Child = headerGrid;
                    ResultsPanel.Children.Add(modHeader);

                    // Create orders list
                    foreach (var order in group.OrderBy(o => o.Platinum))
                    {
                        var orderBorder = new Border
                        {
                            Background = new SolidColorBrush(Color.FromRgb(61, 61, 61)),
                            CornerRadius = new CornerRadius(4),
                            Margin = new Thickness(0, 0, 0, 5),
                            Padding = new Thickness(10)
                        };

                        var orderStack = new StackPanel();
                        orderStack.Children.Add(new TextBox
                        {
                            Text = $"/w {order.User.IngameName} Hi! I want to sell: \"{order.ItemName} (rank {order.Rank})\" for {order.Platinum} platinum. (warframe.market)",
                            Foreground = Brushes.White,
                            Background = new SolidColorBrush(Color.FromRgb(61, 61, 61)),
                            BorderThickness = new Thickness(0),
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap,
                            IsReadOnly = true,
                            IsReadOnlyCaretVisible = true
                        });

                        orderStack.Children.Add(new TextBlock
                        {
                            Text = $"💰 {order.Platinum} platinum • 👤 {order.User.IngameName}",
                            Foreground = new SolidColorBrush(Color.FromRgb(204, 204, 204)),
                            FontSize = 10,
                            Margin = new Thickness(0, 2, 0, 0)
                        });

                        orderBorder.Child = orderStack;
                        ResultsPanel.Children.Add(orderBorder);
                    }

                }
            }
            else
            {
                ResultsPanel.Children.Add(new TextBlock
                {
                    Text = "No orders found for the selected syndicate.",
                    FontSize = 14,
                    Foreground = Brushes.Orange,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(10)
                });
            }

            if (currentModsWithNoOrders.Count > 0)
            {
                var noOrdersHeader = new TextBlock
                {
                    Text = $"Mods with no active buy orders ({currentModsWithNoOrders.Count}):",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.Orange,
                    Margin = new Thickness(0, 20, 0, 10)
                };
                ResultsPanel.Children.Add(noOrdersHeader);

                foreach (var mod in currentModsWithNoOrders)
                {
                    var modBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(61, 61, 61)),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(0, 0, 0, 3),
                        Padding = new Thickness(10)
                    };

                    var modStack = new StackPanel();
                    modStack.Children.Add(new TextBlock
                    {
                        Text = FormatItemName(mod),
                        Foreground = Brushes.White,
                        FontSize = 12
                    });

                    var linkButton = new Button
                    {
                        Content = $"🔗 Check {FormatItemName(mod)} on Market",
                        Background = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                        Foreground = Brushes.White,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 3, 0, 0),
                        FontSize = 10,
                        Cursor = Cursors.Hand
                    };
                    linkButton.Click += (s, e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = GetWarframeMarketUrl(mod),
                        UseShellExecute = true
                    });

                    modStack.Children.Add(linkButton);
                    modBorder.Child = modStack;
                    ResultsPanel.Children.Add(modBorder);
                }
            }
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var resultsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Results");
                if (!Directory.Exists(resultsFolder))
                {
                    Directory.CreateDirectory(resultsFolder);
                }

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var fileName = $"{selectedSyndicateName}_{timestamp}.txt";
                var filePath = Path.Combine(resultsFolder, fileName);

                using (var writer = new StreamWriter(filePath))
                {
                    writer.WriteLine($"========================================");
                    writer.WriteLine($"Warframe Market Standing to Plat Report");
                    writer.WriteLine($"Syndicate: {selectedSyndicateName}");
                    writer.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    writer.WriteLine($"========================================\n");

                    if (currentOrders.Count > 0)
                    {
                        writer.WriteLine($"ORDERS FOUND ({currentOrders.Count} total):");
                        writer.WriteLine($"========================================");
                        var sortedOrders = currentOrders.OrderBy(order => order.Platinum).ToList();
                        
                        var groupedOrders = sortedOrders.GroupBy(o => o.ItemId).ToList();
                        
                        foreach (var group in groupedOrders)
                        {
                            var firstOrder = group.First();
                            var marketUrl = GetWarframeMarketUrl(firstOrder.ItemId);
                            
                            writer.WriteLine($"\n{firstOrder.ItemName}");
                            writer.WriteLine($"Market Link: {marketUrl}");
                            writer.WriteLine("---");
                            
                            foreach (var order in group)
                            {
                                writer.WriteLine($"/w {order.User.IngameName} Hi! I want to sell: \"{order.ItemName} (rank {order.Rank})\" for {order.Platinum} platinum. (warframe.market)");
                            }
                        }
                        writer.WriteLine();
                    }
                    else
                    {
                        writer.WriteLine("No orders found.\n");
                    }

                    if (currentProcessedMods.Count > 0)
                    {
                        writer.WriteLine($"MODS WITH ACTIVE ORDERS ({currentProcessedMods.Count} total):");
                        writer.WriteLine($"========================================");
                        foreach (var mod in currentProcessedMods)
                        {
                            var marketUrl = GetWarframeMarketUrl(mod);
                            writer.WriteLine($"- {FormatItemName(mod)}");
                            writer.WriteLine($"  Link: {marketUrl}");
                        }
                        writer.WriteLine();
                    }

                    if (currentModsWithNoOrders.Count > 0)
                    {
                        writer.WriteLine($"MODS WITH NO ACTIVE BUY ORDERS ({currentModsWithNoOrders.Count} total):");
                        writer.WriteLine($"========================================");
                        foreach (var mod in currentModsWithNoOrders)
                        {
                            var marketUrl = GetWarframeMarketUrl(mod);
                            writer.WriteLine($"- {FormatItemName(mod)}");
                            writer.WriteLine($"  Link: {marketUrl}");
                        }
                        writer.WriteLine();
                    }

                    writer.WriteLine($"========================================");
                    writer.WriteLine($"Report saved at: {filePath}");
                    writer.WriteLine($"========================================");
                }

                StatusText.Text = $"Results exported to: {fileName}";
                MessageBox.Show($"Results exported successfully!\n\nFile: {fileName}\nLocation: {resultsFolder}", 
                              "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Export error: {ex.Message}";
                MessageBox.Show($"Error exporting results: {ex.Message}", 
                              "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FormatItemName(string itemName)
        {
            var formattedName = itemName.Replace("_", " ");
            var words = formattedName.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (!string.IsNullOrEmpty(words[i]))
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                }
            }
            return string.Join(" ", words);
        }

        private string GetWarframeMarketUrl(string itemId)
        {
            return $"https://warframe.market/items/{itemId.Trim()}";
        }

        // Method to handle inline Post Order
        private async void PostOrderInline(string itemId, string itemName, string quantityText, string costText, string rankText)
        {
            try
            {
                if (!int.TryParse(quantityText, out int quantity) || quantity <= 0)
                {
                    StatusText.Text = "❌ Please enter a valid quantity (positive number)";
                    return;
                }

                if (!int.TryParse(costText, out int cost) || cost <= 0)
                {
                    StatusText.Text = "❌ Please enter a valid cost (positive number)";
                    return;
                }

                if (!int.TryParse(rankText, out int rank) || rank < 0)
                {
                    StatusText.Text = "❌ Please enter a valid rank (0 or positive number)";
                    return;
                }

                StatusText.Text = $"🔄 Posting order for {itemName}...";

                // Simulate API call (replace with real implementation)
                await Task.Delay(2000);
                
                StatusText.Text = $"✅ Order posted successfully for {itemName} (Quantity: {quantity}, Cost: {cost}, Rank: {rank})";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error posting order: {ex.Message}";
            }
        }

        // Method to save the last scan data to a JSON file per syndicate
        private void SaveLastScanData()
        {
            try
            {
                var scanData = new
                {
                    SyndicateName = selectedSyndicateName,
                    ScanDate = DateTime.Now,
                    Orders = currentOrders.Select(o => new
                    {
                        o.Id,
                        o.Type,
                        o.Platinum,
                        o.Quantity,
                        o.Rank,
                        o.Visible,
                        o.CreatedAt,
                        o.UpdatedAt,
                        o.ItemId,
                        o.ItemName,
                        User = new { o.User.IngameName, o.User.Status }
                    }).ToList(),
                    ProcessedMods = currentProcessedMods,
                    ModsWithNoOrders = currentModsWithNoOrders
                };

                var lastScanFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LastScans");
                if (!Directory.Exists(lastScanFolder))
                {
                    Directory.CreateDirectory(lastScanFolder);
                }

                // Create filename based on syndicate name (replace spaces with underscores)
                var safeSyndicateName = selectedSyndicateName.Replace(" ", "_").Replace(":", "").Replace("/", "");
                lastScanFilePath = Path.Combine(lastScanFolder, $"{safeSyndicateName}_last_scan.json");
                var json = System.Text.Json.JsonSerializer.Serialize(scanData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(lastScanFilePath, json);

                StatusText.Text += $"\n💾 {selectedSyndicateName} scan data saved to: {Path.GetFileName(lastScanFilePath)}";
            }
            catch (Exception ex)
            {
                StatusText.Text += $"\n⚠️ Failed to save scan data: {ex.Message}";
            }
        }

        // Method to load old scan data
        private void LoadOldDataButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var lastScanFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LastScans");
                
                if (!Directory.Exists(lastScanFolder))
                {
                    MessageBox.Show("No previous scan data found.\n\nPlease run a scan first to save data.", 
                                  "No Data Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Get all saved scan files
                var scanFiles = Directory.GetFiles(lastScanFolder, "*_last_scan.json");
                
                if (scanFiles.Length == 0)
                {
                    MessageBox.Show("No previous scan data found.\n\nPlease run a scan first to save data.", 
                                  "No Data Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // If only one file, load it directly
                if (scanFiles.Length == 1)
                {
                    LoadScanFromFile(scanFiles[0]);
                    return;
                }

                // Show selection dialog for multiple files
                var fileNames = scanFiles.Select(f => Path.GetFileNameWithoutExtension(f).Replace("_last_scan", "")).ToList();
                var selectionDialog = new SyndicateSelectionDialog(fileNames);
                
                if (selectionDialog.ShowDialog() == true)
                {
                    var selectedFile = scanFiles[selectionDialog.SelectedIndex];
                    LoadScanFromFile(selectedFile);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading old data: {ex.Message}", 
                              "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = $"Error loading old data: {ex.Message}";
            }
        }

        // Helper method to load scan data from a specific file
        private void LoadScanFromFile(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
                var root = jsonDoc.RootElement;

                // Clear current data
                currentOrders.Clear();
                currentProcessedMods.Clear();
                currentModsWithNoOrders.Clear();

                // Load syndicate name
                if (root.TryGetProperty("SyndicateName", out var syndicateNameElement))
                {
                    selectedSyndicateName = syndicateNameElement.GetString() ?? "";
                }

                // Load orders
                if (root.TryGetProperty("Orders", out var ordersElement))
                {
                    foreach (var orderElement in ordersElement.EnumerateArray())
                    {
                        var order = new Order();
                        if (orderElement.TryGetProperty("Id", out var idElement)) order.Id = idElement.GetString() ?? "";
                        if (orderElement.TryGetProperty("Type", out var typeElement)) order.Type = typeElement.GetString() ?? "";
                        if (orderElement.TryGetProperty("Platinum", out var platinumElement)) order.Platinum = platinumElement.GetInt32();
                        if (orderElement.TryGetProperty("Quantity", out var quantityElement)) order.Quantity = quantityElement.GetInt32();
                        if (orderElement.TryGetProperty("Rank", out var rankElement)) order.Rank = rankElement.GetInt32();
                        if (orderElement.TryGetProperty("Visible", out var visibleElement)) order.Visible = visibleElement.GetBoolean();
                        if (orderElement.TryGetProperty("ItemId", out var itemIdElement)) order.ItemId = itemIdElement.GetString() ?? "";
                        if (orderElement.TryGetProperty("ItemName", out var itemNameElement)) order.ItemName = itemNameElement.GetString() ?? "";
                        
                        if (orderElement.TryGetProperty("User", out var userElement))
                        {
                            order.User = new User();
                            if (userElement.TryGetProperty("IngameName", out var ingameNameElement)) order.User.IngameName = ingameNameElement.GetString() ?? "";
                            if (userElement.TryGetProperty("Status", out var statusElement)) order.User.Status = statusElement.GetString() ?? "";
                        }

                        currentOrders.Add(order);
                    }
                }

                // Load processed mods
                if (root.TryGetProperty("ProcessedMods", out var processedModsElement))
                {
                    foreach (var modElement in processedModsElement.EnumerateArray())
                    {
                        currentProcessedMods.Add(modElement.GetString() ?? "");
                    }
                }

                // Load mods with no orders
                if (root.TryGetProperty("ModsWithNoOrders", out var modsWithNoOrdersElement))
                {
                    foreach (var modElement in modsWithNoOrdersElement.EnumerateArray())
                    {
                        currentModsWithNoOrders.Add(modElement.GetString() ?? "");
                    }
                }

                // Display the loaded data
                DisplayResults();
                ExportButton.IsEnabled = true;

                // Get scan date
                var scanDate = "Unknown";
                if (root.TryGetProperty("ScanDate", out var scanDateElement))
                {
                    if (DateTime.TryParse(scanDateElement.GetString(), out var date))
                    {
                        scanDate = date.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }

                StatusText.Text = $"📂 Loaded old scan data from {scanDate}\nFound {currentOrders.Count} orders for {currentProcessedMods.Count} mods";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading old data: {ex.Message}", 
                              "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = $"Error loading old data: {ex.Message}";
            }
        }
    }
}
