﻿using Office365RESTExplorerforSites.Common;
using Office365RESTExplorerforSites.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Input;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using System.Text;
using Windows.Storage;
using Microsoft.IdentityModel.Clients.ActiveDirectory;

using Windows.Data.Json;
using Newtonsoft.Json;
using System.Net;

// The Split Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234234

namespace Office365RESTExplorerforSites
{
    /// <summary>
    /// A page that displays a group title, a list of items within the group, and details for
    /// the currently selected item.
    /// </summary>
    public sealed partial class SplitPage : Page
    {
        private NavigationHelper navigationHelper;
        private ObservableDictionary defaultViewModel = new ObservableDictionary();

        private HttpWebRequest endpointRequest;
        private HttpWebResponse endpointResponse;

        /// <summary>
        /// NavigationHelper is used on each page to aid in navigation and 
        /// process lifetime management
        /// </summary>
        public NavigationHelper NavigationHelper
        {
            get { return this.navigationHelper; }
        }

        /// <summary>
        /// This can be changed to a strongly typed view model.
        /// </summary>
        public ObservableDictionary DefaultViewModel
        {
            get { return this.defaultViewModel; }
        }

        public SplitPage()
        {
            this.InitializeComponent();

            // Setup the navigation helper
            this.navigationHelper = new NavigationHelper(this);
            this.navigationHelper.LoadState += navigationHelper_LoadState;
            this.navigationHelper.SaveState += navigationHelper_SaveState;

            // Setup the logical page navigation components that allow
            // the page to only show one pane at a time.
            this.navigationHelper.GoBackCommand = new RelayCommand(() => this.GoBack(), () => this.CanGoBack());
            this.itemListView.SelectionChanged += ItemListView_SelectionChanged;

            // Start listening for Window size changes 
            // to change from showing two panes to showing a single pane
            Window.Current.SizeChanged += Window_SizeChanged;
            this.InvalidateVisualState();

            this.Unloaded += SplitPage_Unloaded;

            //TODO: Change this to databind
            this.SPSiteLink.NavigateUri = new Uri(ApplicationData.Current.LocalSettings.Values["ServiceResourceId"].ToString());
            this.SPSiteLink.Content = ApplicationData.Current.LocalSettings.Values["ServiceResourceId"].ToString();
        }

        /// <summary>
        /// Unhook from the SizedChanged event when the SplitPage is Unloaded.
        /// </summary>
        private void SplitPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Window.Current.SizeChanged -= Window_SizeChanged;
        }

        /// <summary>
        /// Populates the page with content passed during navigation.  Any saved state is also
        /// provided when recreating a page from a prior session.
        /// </summary>
        /// <param name="sender">
        /// The source of the event; typically <see cref="NavigationHelper"/>
        /// </param>
        /// <param name="e">Event data that provides both the navigation parameter passed to
        /// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested and
        /// a dictionary of state preserved by this page during an earlier
        /// session.  The state will be null the first time a page is visited.</param>
        private async void navigationHelper_LoadState(object sender, LoadStateEventArgs e)
        {
            var group = await DataSource.GetGroupAsync((String)e.NavigationParameter);
            this.DefaultViewModel["Group"] = group;
            this.DefaultViewModel["Items"] = group.Items;

            if (e.PageState == null)
            {
                this.itemListView.SelectedItem = null;
                // When this is a new page, select the first item automatically unless logical page
                // navigation is being used (see the logical page navigation #region below.)
                if (!this.UsingLogicalPageNavigation() && this.itemsViewSource.View != null)
                {
                    this.itemsViewSource.View.MoveCurrentToFirst();
                }
            }
            else
            {
                // Restore the previously saved state associated with this page
                if (e.PageState.ContainsKey("SelectedItem") && this.itemsViewSource.View != null)
                {
                    var selectedItem = await DataSource.GetItemAsync((String)e.PageState["SelectedItem"]);
                    this.itemsViewSource.View.MoveCurrentTo(selectedItem);
                }
            }
        }

        /// <summary>
        /// Preserves state associated with this page in case the application is suspended or the
        /// page is discarded from the navigation cache.  Values must conform to the serialization
        /// requirements of <see cref="SuspensionManager.SessionState"/>.
        /// </summary>
        /// <param name="navigationParameter">The parameter value passed to
        /// <see cref="Frame.Navigate(Type, Object)"/> when this page was initially requested.
        /// </param>
        /// <param name="sender">The source of the event; typically <see cref="NavigationHelper"/></param>
        /// <param name="e">Event data that provides an empty dictionary to be populated with
        /// serializable state.</param>
        private void navigationHelper_SaveState(object sender, SaveStateEventArgs e)
        {
            if (this.itemsViewSource.View != null)
            {
                var selectedItem = (Data.DataItem)this.itemsViewSource.View.CurrentItem;
                if (selectedItem != null) e.PageState["SelectedItem"] = selectedItem.UniqueId;
            }
        }

        #region Logical page navigation

        // The split page isdesigned so that when the Window does have enough space to show
        // both the list and the dteails, only one pane will be shown at at time.
        //
        // This is all implemented with a single physical page that can represent two logical
        // pages.  The code below achieves this goal without making the user aware of the
        // distinction.

        private const int MinimumWidthForSupportingTwoPanes = 768;

        /// <summary>
        /// Invoked to determine whether the page should act as one logical page or two.
        /// </summary>
        /// <returns>True if the window should show act as one logical page, false
        /// otherwise.</returns>
        private bool UsingLogicalPageNavigation()
        {
            return Window.Current.Bounds.Width < MinimumWidthForSupportingTwoPanes;
        }

        /// <summary>
        /// Invoked with the Window changes size
        /// </summary>
        /// <param name="sender">The current Window</param>
        /// <param name="e">Event data that describes the new size of the Window</param>
        private void Window_SizeChanged(object sender, Windows.UI.Core.WindowSizeChangedEventArgs e)
        {
            this.InvalidateVisualState();
        }

        /// <summary>
        /// Invoked when an item within the list is selected.
        /// </summary>
        /// <param name="sender">The GridView displaying the selected item.</param>
        /// <param name="e">Event data that describes how the selection was changed.</param>
        private void ItemListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Invalidate the view state when logical page navigation is in effect, as a change
            // in selection may cause a corresponding change in the current logical page.  When
            // an item is selected this has the effect of changing from displaying the item list
            // to showing the selected item's details.  When the selection is cleared this has the
            // opposite effect.
            if (this.UsingLogicalPageNavigation()) this.InvalidateVisualState();

            itemDetail.Visibility = Windows.UI.Xaml.Visibility.Visible;
            responseViewer.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
        }

        private bool CanGoBack()
        {
            if (this.UsingLogicalPageNavigation() && this.itemListView.SelectedItem != null)
            {
                return true;
            }
            else
            {
                return this.navigationHelper.CanGoBack();
            }
        }
        private void GoBack()
        {
            if (this.UsingLogicalPageNavigation() && this.itemListView.SelectedItem != null)
            {
                // When logical page navigation is in effect and there's a selected item that
                // item's details are currently displayed.  Clearing the selection will return to
                // the item list.  From the user's point of view this is a logical backward
                // navigation.
                this.itemListView.SelectedItem = null;
            }
            else
            {
                this.navigationHelper.GoBack();
            }
        }

        private void InvalidateVisualState()
        {
            var visualState = DetermineVisualState();
            VisualStateManager.GoToState(this, visualState, false);
            this.navigationHelper.GoBackCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Invoked to determine the name of the visual state that corresponds to an application
        /// view state.
        /// </summary>
        /// <returns>The name of the desired visual state.  This is the same as the name of the
        /// view state except when there is a selected item in portrait and snapped views where
        /// this additional logical page is represented by adding a suffix of _Detail.</returns>
        private string DetermineVisualState()
        {
            if (!UsingLogicalPageNavigation())
                return "PrimaryView";

            // Update the back button's enabled state when the view state changes
            var logicalPageBack = this.UsingLogicalPageNavigation() && this.itemListView.SelectedItem != null;

            return logicalPageBack ? "SinglePane_Detail" : "SinglePane";
        }

        #endregion

        #region NavigationHelper registration

        /// The methods provided in this section are simply used to allow
        /// NavigationHelper to respond to the page's navigation methods.
        /// 
        /// Page specific logic should be placed in event handlers for the  
        /// <see cref="GridCS.Common.NavigationHelper.LoadState"/>
        /// and <see cref="GridCS.Common.NavigationHelper.SaveState"/>.
        /// The navigation parameter is available in the LoadState method 
        /// in addition to page state preserved during an earlier session.

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            navigationHelper.OnNavigatedTo(e);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            navigationHelper.OnNavigatedFrom(e);
        }

        #endregion

        private async void sendRequest_Click(object sender, RoutedEventArgs e)
        {
            string accessToken = null;
            bool thereIsAccessToken = false;

            // Validate that I have an access token
            try
            {
                accessToken = ApplicationData.Current.LocalSettings.Values["AccessToken"].ToString();
                thereIsAccessToken = true;
            }
            catch (NullReferenceException)
            {
                thereIsAccessToken = false;
            }
            if (!thereIsAccessToken)
            {
                await Office365Helper.AcquireAccessToken(ApplicationData.Current.LocalSettings.Values["ServiceResourceId"].ToString());
                //Refresh the data source, the Authorization header in the data surce needs to be updated
                DataGroup currentGroup = (DataGroup)this.defaultViewModel["Group"];
                DataGroup newGroup = await DataSource.GetGroupAsync(currentGroup.UniqueId);
                this.DefaultViewModel["Group"] = newGroup;
                this.DefaultViewModel["Items"] = newGroup.Items;
            }

            string method;

            method = methodSwitch.IsOn ? "POST" : "GET";

            //Validate that the resulting URI is well-formed.
            Uri endpointUri = new Uri(new Uri(ApplicationData.Current.LocalSettings.Values["ServiceResourceId"].ToString()), endpointText.Text);
            endpointRequest = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(endpointUri.AbsoluteUri);

            endpointRequest.Method = method;

            JsonObject headers = JsonObject.Parse(headersText.Text);

            foreach (KeyValuePair<string, IJsonValue> header in headers)
            {
                switch (header.Key.ToLower())
                {
                    case "accept":
                        endpointRequest.Accept = header.Value.GetString();
                        break;
                    case "content-type":
                        endpointRequest.ContentType = header.Value.GetString();
                        break;
                    default:
                        endpointRequest.Headers[header.Key] = header.Value.GetString();
                        break;
                }
            }

            //Request body, only if method is POST
            if (String.Compare(method, "post", StringComparison.CurrentCultureIgnoreCase) == 0)
            {
                string postData = bodyText.Text;
                UTF8Encoding encoding = new UTF8Encoding();
                byte[] byte1 = encoding.GetBytes(postData);
                Stream newStream = await endpointRequest.GetRequestStreamAsync();
                newStream.Write(byte1, 0, byte1.Length);
            }
            
            Stream responseStream;
            WebHeaderCollection responseHeaders;
            try
            {
                endpointResponse = (HttpWebResponse) await endpointRequest.GetResponseAsync();
                responseStatusText.Text = (int)endpointResponse.StatusCode + " - " + endpointResponse.StatusDescription;
                responseStream = endpointResponse.GetResponseStream();
                responseUriText.Text = endpointResponse.ResponseUri.AbsoluteUri;
                responseHeaders = endpointResponse.Headers;
            }
            catch (WebException we)
            {
                //TODO: Need to check for a condition that tells me that the token is invalid.
                // We may need to reset the token
                responseStatusText.Text = we.Message;
                responseStream = we.Response.GetResponseStream();
                responseUriText.Text = we.Response.ResponseUri.AbsoluteUri;
                responseHeaders = we.Response.Headers;
            }

            //Process response
            int responseLength = 100000;
            byte[] responseBytes = new byte[responseLength];

            //TODO: Can I just do  responseStream.Flush(); await responseStream.ReadAsync(responseBytes, 0, responseLength); Or perhaps pass the stream to a StreamReader or something?
            for (int i = 0; responseStream.CanRead; i++)
            {
                byte[] buffer = new byte[1];
                await responseStream.ReadAsync(buffer, 0, 1);

                if (buffer[0] != 0)
                    responseBytes[i] = buffer[0];
                else
                    break;
            }

            string responseString = Encoding.UTF8.GetString(responseBytes, 0, responseLength);
            responseString = responseString.Substring(0, responseString.IndexOf('\0'));

            JsonObject responseJson;
            if (String.IsNullOrEmpty(responseString))
                responseJson = new JsonObject();
            else if (JsonObject.TryParse(responseString, out responseJson))
                responseText.Text = JsonConvert.SerializeObject(responseJson, Formatting.Indented);
            else
                responseText.Text = responseString;
            
            JsonObject jsonHeaders = new JsonObject();
            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.Formatting = Formatting.Indented;
            for (int i = 0; i < responseHeaders.Count; i++)
            {
                string key = responseHeaders.AllKeys[i].ToString();
                jsonHeaders.Add(key, JsonValue.CreateStringValue(responseHeaders[key]));
            }
            responseHeadersText.Text = JsonConvert.SerializeObject(jsonHeaders, Formatting.Indented);
            
            itemDetail.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            responseViewer.Visibility = Windows.UI.Xaml.Visibility.Visible;
        }
    }
}