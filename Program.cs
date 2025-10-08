using System.Net.Http.Json;
using System.Text.RegularExpressions;

class Program
{
    private static string API_URL = "https://api.warframe.market/v2/orders/item/";
    private static int MAX_ORDERS_PER_MOD = 5;  // Max standing w/each syndicate is 132,000 and each mod costs 25,000 so there's no point in fetching more than 5 orders per mod.
    private static int DELAY_BETWEEN_REQUESTS = 200;   // 1 second delay between each request to avoid DDoSing the Warframe Market lol

    private static string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
    private static string modsFolderPath = Path.Combine(appDirectory, "WarframeSyndicateMods");
    private static string resultsFolder = Path.Combine(appDirectory, "Results");

    static async Task Main(string[] args)
    {
        // Create Results folder if it doesn't exist
        if (!Directory.Exists(resultsFolder))
        {
            Directory.CreateDirectory(resultsFolder);
        }

        // Dynamically fetch the list of syndicates from the WarframeSyndicateMods folder.
        List<(string Name, string ModsFile)> syndicates = GetSyndicates();
        if (syndicates.Count == 0)
        {
            Console.WriteLine("No files found in the WarframeSyndicateMods folder. Exiting.");
            return;
        }

        // Prompt the user to select a syndicate.
        Console.WriteLine("Select the syndicate you have standing with:");
        for (int i = 0; i < syndicates.Count; i++)
        {
            Console.WriteLine($"{i + 1}. {syndicates[i].Name}");
        }

        Console.Write("Enter the number of your choice: ");
        if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > syndicates.Count)
        {
            Console.WriteLine("Invalid selection. Exiting.");
            return;
        }

        var selectedSyndicate = syndicates[choice - 1];
        Console.WriteLine($"You selected: {selectedSyndicate.Name}");

        // Read the file and fetch the list of mods for the selected syndicate.
        if (!File.Exists(selectedSyndicate.ModsFile))
        {
            Console.WriteLine($"File {selectedSyndicate.ModsFile} not found. Exiting.");
            return;
        }

        var mods = File.ReadAllLines(Path.Combine(modsFolderPath, Path.GetFileName(selectedSyndicate.ModsFile)));
        if (mods.Length == 0)
        {
            Console.WriteLine($"No mods found in {selectedSyndicate.ModsFile}. Exiting.");
            return;
        }

        Console.WriteLine($"Found {mods.Length} mod(s) to search for in {selectedSyndicate.ModsFile}.");

        // Fetch the orders for the mods from the Warframe Market.
        var (orders, processedMods, modsWithNoOrders) = await FetchOrders(mods);

        // Save results to log file
        SaveResultsToFile(selectedSyndicate.Name, orders, processedMods, modsWithNoOrders);

        // Print the found orders as messages ready to paste in the ingame chat.
        PrintOrders(orders, modsWithNoOrders);

        // Update the syndicate file with newly processed mods
        UpdateSyndicateFile(selectedSyndicate.ModsFile, processedMods);

        Console.WriteLine("\nPress Enter to exit...");
        Console.ReadLine();
    }

    // Method to get the list of syndicates dynamically from the WarframeSyndicateMods folder.
    private static List<(string, string)> GetSyndicates()
    {
        var syndicates = new List<(string Name, string ModsFile)>();

        string[] modFiles = Directory.GetFiles(modsFolderPath, "*.txt");

        foreach (var file in modFiles)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            
            // Regex to separate each word from the file name. (Ex. RedVeil -> Red Veil)
            string syndicateName = Regex.Replace(fileName, "(\\B[A-Z])", " $1");

            syndicates.Add((syndicateName, file));
        }

        return syndicates;
    }

    // Method to fetch the buy orders from the Warframe Market.
    private static async Task<(List<Order> orders, List<string> processedMods, List<string> modsWithNoOrders)> FetchOrders(string[] mods)
    {
        var orders = new List<Order>();
        var processedMods = new List<string>();
        var modsWithNoOrders = new List<string>();

        using HttpClient client = new();

        foreach (var mod in mods)
        {
            var url = API_URL + mod.Trim();
            Console.WriteLine($"\nFetching data for mod: {mod}");

            try
            {
                var response = await client.GetFromJsonAsync<WarframeMarketResponse>(url);

                if (response?.Data != null && response.Data.Length > 0)
                {
                    // Only fetch visible buy orders from online-ingame users who seek to buy rank 0 mods for convenience.
                    var sortedOrders = response.Data
                        .Where(order =>
                            order.Visible &&
                            order.Type == "sell" &&
                            order.User.Status == "ingame" &&
                            order.Rank == 0)
                        .OrderByDescending(order => order.Platinum)
                        .Take(MAX_ORDERS_PER_MOD)
                        .Select(order =>
                        {
                            order.ItemId = mod.Trim();  // Set ItemId from the text file
                            order.ItemName = FormatItemName(mod);
                            return order;
                        })
                        .ToList();

                    if (sortedOrders.Count > 0)
                    {
                        orders.AddRange(sortedOrders);
                        processedMods.Add(mod.Trim());
                        Console.WriteLine($"Found {sortedOrders.Count} order(s) for {mod}");
                    }
                    else
                    {
                        Console.WriteLine($"No matching orders found for mod: {mod}");
                        modsWithNoOrders.Add(mod.Trim());
                    }
                }
                else
                {
                    Console.WriteLine($"No data available for mod: {mod}");
                    modsWithNoOrders.Add(mod.Trim());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while fetching data for mod {mod}: {ex.Message}");
                modsWithNoOrders.Add(mod.Trim());
            }

            await Task.Delay(DELAY_BETWEEN_REQUESTS);
        }

        return (orders, processedMods, modsWithNoOrders);
    }

    // Method to print the list of orders sorted by platinum ASC as messages ready to paste into the ingame chat.
    private static void PrintOrders(List<Order> orders, List<string> modsWithNoOrders)
    {
        var sortedOrders = orders.OrderBy(order => order.Platinum).ToList();

        if (orders.Count > 0)
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("ORDERS FETCHED SUCCESSFULLY:");
            Console.WriteLine("========================================");
            
            // Group orders by item to show market links
            var groupedOrders = sortedOrders.GroupBy(o => o.ItemId).ToList();
            
            foreach (var group in groupedOrders)
            {
                var firstOrder = group.First();
                var marketUrl = GetWarframeMarketUrl(firstOrder.ItemId);
                
                Console.WriteLine($"\n{firstOrder.ItemName}");
                Console.WriteLine($"Market Link: {marketUrl}");
                Console.WriteLine("---");
                
                foreach (var order in group)
                {
                    Console.WriteLine($"/w {order.User.IngameName} Hi! I want to sell: \"{order.ItemName} (rank {order.Rank})\" for {order.Platinum} platinum. (warframe.market)");
                }
            }
            
            Console.WriteLine($"\n========================================");
            Console.WriteLine($"Total orders found: {orders.Count}");
            Console.WriteLine($"========================================");
        }
        else
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("No orders found for the selected syndicate.");
            Console.WriteLine("========================================");
        }

        if (modsWithNoOrders.Count > 0)
        {
            Console.WriteLine("\n========================================");
            Console.WriteLine("MODS WITH NO ACTIVE BUY ORDERS:");
            Console.WriteLine("========================================");
            foreach (var mod in modsWithNoOrders)
            {
                var marketUrl = GetWarframeMarketUrl(mod);
                Console.WriteLine($"- {FormatItemName(mod)}");
                Console.WriteLine($"  Link: {marketUrl}");
            }
            Console.WriteLine($"\nTotal mods without orders: {modsWithNoOrders.Count}");
        }
    }

    // Method to save results to a log file for future reference
    private static void SaveResultsToFile(string syndicateName, List<Order> orders, List<string> processedMods, List<string> modsWithNoOrders)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var fileName = $"{syndicateName}_{timestamp}.txt";
        var filePath = Path.Combine(resultsFolder, fileName);

        using (StreamWriter writer = new StreamWriter(filePath))
        {
            writer.WriteLine($"========================================");
            writer.WriteLine($"Warframe Market Standing to Plat Report");
            writer.WriteLine($"Syndicate: {syndicateName}");
            writer.WriteLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"========================================\n");

            if (orders.Count > 0)
            {
                writer.WriteLine($"ORDERS FOUND ({orders.Count} total):");
                writer.WriteLine($"========================================");
                var sortedOrders = orders.OrderBy(order => order.Platinum).ToList();
                
                // Group orders by item to show market links
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

            if (processedMods.Count > 0)
            {
                writer.WriteLine($"MODS WITH ACTIVE ORDERS ({processedMods.Count} total):");
                writer.WriteLine($"========================================");
                foreach (var mod in processedMods)
                {
                    var marketUrl = GetWarframeMarketUrl(mod);
                    writer.WriteLine($"- {FormatItemName(mod)}");
                    writer.WriteLine($"  Link: {marketUrl}");
                }
                writer.WriteLine();
            }

            if (modsWithNoOrders.Count > 0)
            {
                writer.WriteLine($"MODS WITH NO ACTIVE BUY ORDERS ({modsWithNoOrders.Count} total):");
                writer.WriteLine($"========================================");
                foreach (var mod in modsWithNoOrders)
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

        Console.WriteLine($"\n\nResults saved to: {filePath}");
    }

    // Method to update the syndicate file with newly processed mods (appends if not already present)
    private static void UpdateSyndicateFile(string syndicateFile, List<string> processedMods)
    {
        if (processedMods.Count == 0)
        {
            return;
        }

        try
        {
            // Read existing mods from file
            var existingMods = new HashSet<string>(File.ReadAllLines(syndicateFile).Select(m => m.Trim().ToLower()));
            
            // Find new mods that aren't in the file yet
            var newMods = processedMods.Where(mod => !existingMods.Contains(mod.ToLower())).ToList();

            if (newMods.Count > 0)
            {
                // Append new mods to the file
                using (StreamWriter writer = File.AppendText(syndicateFile))
                {
                    foreach (var mod in newMods)
                    {
                        writer.WriteLine(mod);
                    }
                }

                Console.WriteLine($"\n{newMods.Count} new mod(s) added to {Path.GetFileName(syndicateFile)}:");
                foreach (var mod in newMods)
                {
                    Console.WriteLine($"- {FormatItemName(mod)}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating syndicate file: {ex.Message}");
        }
    }

    // Method to format the id of the item into the actual name of the item. (Ex. accumulating_whipclaw -> Accumulating Whipclaw)
    public static string FormatItemName(string itemName)
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

    // Method to generate the Warframe Market URL for a given item
    private static string GetWarframeMarketUrl(string itemId)
    {
        return $"https://warframe.market/items/{itemId.Trim()}";
    }
}
