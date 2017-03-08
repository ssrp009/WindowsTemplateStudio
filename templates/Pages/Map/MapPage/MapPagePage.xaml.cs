using System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Maps;

// The Map Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace ItemNamespace.MapPage
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MapPagePage : Page
    {
        public MapPagePage()
        {
            this.InitializeComponent();
        }

        private void OnLoaded(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            MapControl map = sender as MapControl;
            if (map == null)
            {
                throw new ArgumentNullException("Expected type is MapControl");
            }
            
            ViewModel.SetMap(map);
        }
    }
}
