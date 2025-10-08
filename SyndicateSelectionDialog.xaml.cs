using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace WarframeMarket_StandingToPlat
{
    public partial class SyndicateSelectionDialog : Window
    {
        public int SelectedIndex { get; private set; } = -1;

        public SyndicateSelectionDialog(List<string> syndicateNames)
        {
            InitializeComponent();
            SyndicateListBox.ItemsSource = syndicateNames;
        }

        private void SyndicateListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            LoadButton.IsEnabled = SyndicateListBox.SelectedIndex >= 0;
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedIndex = SyndicateListBox.SelectedIndex;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
