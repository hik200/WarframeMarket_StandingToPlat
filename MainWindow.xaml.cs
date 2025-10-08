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

        public MainWindow()
        {
            InitializeComponent();
            LoadSyndicates();
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

                    // Create mod header
                    var modHeader = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(0, 0, 0, 10),
                        Padding = new Thickness(15)
                    };

                    var modStack = new StackPanel();
                    modStack.Children.Add(new TextBlock
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

                    modStack.Children.Add(linkButton);
                    modHeader.Child = modStack;
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
                        orderStack.Children.Add(new TextBlock
                        {
                            Text = $"/w {order.User.IngameName} Hi! I want to sell: \"{order.ItemName} (rank {order.Rank})\" for {order.Platinum} platinum. (warframe.market)",
                            Foreground = Brushes.White,
                            FontSize = 12,
                            TextWrapping = TextWrapping.Wrap
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
    }
}
