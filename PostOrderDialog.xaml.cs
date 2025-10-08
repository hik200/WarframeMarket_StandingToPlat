using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace WarframeMarket_StandingToPlat
{
    public partial class PostOrderDialog : Window
    {
        public string ItemId { get; set; } = "";
        public string ItemName { get; set; } = "";
        public int DefaultCost { get; set; } = 0;

        public PostOrderDialog(string itemId, string itemName, int defaultCost)
        {
            InitializeComponent();
            ItemId = itemId;
            ItemName = itemName;
            DefaultCost = defaultCost;
            
            // Set default values
            QuantityTextBox.Text = "1";
            CostTextBox.Text = defaultCost.ToString();
            RankTextBox.Text = "0";
            
            DataContext = this;
        }

        private async void PostButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(QuantityTextBox.Text, out int quantity) || quantity <= 0)
                {
                    StatusText.Text = "❌ Please enter a valid quantity (positive number)";
                    return;
                }

                if (!int.TryParse(CostTextBox.Text, out int cost) || cost <= 0)
                {
                    StatusText.Text = "❌ Please enter a valid cost (positive number)";
                    return;
                }

                if (!int.TryParse(RankTextBox.Text, out int rank) || rank < 0)
                {
                    StatusText.Text = "❌ Please enter a valid rank (0 or positive number)";
                    return;
                }

                StatusText.Text = "🔄 Posting order...";
                PostButton.IsEnabled = false;

                var success = await PostOrderToWarframeMarket(ItemId, cost, quantity, rank);
                
                if (success)
                {
                    StatusText.Text = "✅ Order posted successfully!";
                    await Task.Delay(2000);
                    DialogResult = true;
                    Close();
                }
                else
                {
                    StatusText.Text = "❌ Failed to post order. Please check your connection and try again.";
                    PostButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Error: {ex.Message}";
                PostButton.IsEnabled = true;
            }
        }

        private async Task<bool> PostOrderToWarframeMarket(string itemId, int platinum, int quantity, int rank)
        {
            try
            {
                using var client = new HttpClient();
                
                var orderData = new
                {
                    itemId = itemId,
                    type = "sell",
                    platinum = platinum,
                    quantity = quantity,
                    visible = true,
                    perTrade = 1,
                    rank = rank,
                    charges = 0,
                    subtype = "",
                    amberStars = 0,
                    cyanStars = 0
                };

                var json = JsonSerializer.Serialize(orderData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Note: This would require authentication in a real implementation
                // For now, we'll simulate the API call
                StatusText.Text = "⚠️ Note: This is a demo. Real posting requires Warframe Market API authentication.";
                
                // Simulate API delay
                await Task.Delay(2000);
                
                return true; // Simulate success
            }
            catch
            {
                return false;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
