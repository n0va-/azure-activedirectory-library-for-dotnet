//------------------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.
// All rights reserved.
//
// This code is licensed under the MIT License.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//------------------------------------------------------------------------------

using CoreFoundation;
using Foundation;
using Microsoft.Identity.Core.Helpers;
using System;
using System.Collections.Generic;
using UIKit;
using WebKit;

namespace Microsoft.Identity.Core.UI.EmbeddedWebview
{
    [Foundation.Register("AuthenticationAgentUIViewController")]
    internal class AuthenticationAgentUIViewController : UIViewController
    {
        private const string AboutBlankUri = "about:blank";

        private WKWebView wkWebView;

        private readonly string url;
        private readonly string callback;

        private readonly ReturnCodeCallback callbackMethod;

        public delegate void ReturnCodeCallback(AuthorizationResult result);

        public AuthenticationAgentUIViewController(string url, string callback, ReturnCodeCallback callbackMethod)
        {
            this.url = url;
            this.callback = callback;
            this.callbackMethod = callbackMethod;
            NSUrlProtocol.RegisterClass(new ObjCRuntime.Class(typeof(CoreCustomUrlProtocol)));
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            View.BackgroundColor = UIColor.White;

            WKWebViewConfiguration webConfiguration = new WKWebViewConfiguration();

            wkWebView = new WKWebView(View.Bounds, webConfiguration);
            NSUrl url = wkWebView.Url;

            wkWebView.LoadRequest(new NSUrlRequest(url));

            if (LoadView(url) == true)
            {
                wkWebView.StopLoading();

                // If the title is too long, iOS automatically truncates it and adds ...
                this.Title = wkWebView.EvaluateJavaScriptAsync(@"document.title").ToString();

                View.AddSubview(wkWebView);

                this.NavigationItem.LeftBarButtonItem = new UIBarButtonItem(UIBarButtonSystemItem.Cancel,
                    this.CancelAuthentication);

                wkWebView.LoadRequest(new NSUrlRequest(new NSUrl(this.url)));

                // if this is false, page will be 'zoomed in' to normal size
                //webView.ScalesPageToFit = true;
            }
        }

        private bool LoadView(NSUrl url)
        {
            string requestUrlString = url.BaseUrl.ToString();

            // If the URL has the browser:// scheme then this is a request to open an external browser
            if (requestUrlString.StartsWith(BrokerConstants.BrowserExtPrefix, StringComparison.OrdinalIgnoreCase))
            {
                DispatchQueue.MainQueue.DispatchAsync(() => CancelAuthentication(null, null));

                // Build the HTTPS URL for launching with an external browser
                var httpsUrlBuilder = new UriBuilder(requestUrlString)
                {
                    Scheme = Uri.UriSchemeHttps
                };
                requestUrlString = httpsUrlBuilder.Uri.AbsoluteUri;

                DispatchQueue.MainQueue.DispatchAsync(
                    () => UIApplication.SharedApplication.OpenUrl(new NSUrl(requestUrlString)));
                this.DismissViewController(true, null);
                return false;
            }

            if (requestUrlString.StartsWith(callback, StringComparison.OrdinalIgnoreCase) ||
                requestUrlString.StartsWith(BrokerConstants.BrowserExtInstallPrefix, StringComparison.OrdinalIgnoreCase))
            {
                callbackMethod(new AuthorizationResult(AuthorizationStatus.Success, url.BaseUrl.ToString()));
                this.DismissViewController(true, null);
                return false;
            }

            if (requestUrlString.StartsWith(BrokerConstants.DeviceAuthChallengeRedirect, StringComparison.OrdinalIgnoreCase))
            {
                Uri uri = new Uri(requestUrlString);
                string query = uri.Query;
                if (query.StartsWith("?", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Substring(1);
                }

                Dictionary<string, string> keyPair = CoreHelpers.ParseKeyValueList(query, '&', true, false, null);
                string responseHeader = DeviceAuthHelper.CreateDeviceAuthChallengeResponse(keyPair).Result;

                NSMutableUrlRequest newRequest = (NSMutableUrlRequest)url.MutableCopy();
                newRequest.Url = new NSUrl(keyPair["SubmitUrl"]);
                newRequest[BrokerConstants.ChallengeResponseHeader] = responseHeader;
                wkWebView.LoadRequest(newRequest);
                return false;
            }

            if (!url.BaseUrl.AbsoluteString.Equals(AboutBlankUri, StringComparison.OrdinalIgnoreCase)
             && !url.BaseUrl.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                AuthorizationResult result = new AuthorizationResult(AuthorizationStatus.ErrorHttp);
                result.Error = MsalError.NonHttpsRedirectNotSupported;
                result.ErrorDescription = MsalErrorMessage.NonHttpsRedirectNotSupported;
                callbackMethod(result);
                this.DismissViewController(true, null);
                return false;
            }

            return true;
        }

        private void CancelAuthentication(object sender, EventArgs e)
        {
            callbackMethod(new AuthorizationResult(AuthorizationStatus.UserCancel, null));
            this.DismissViewController(true, null);
        }

        public override void DismissViewController(bool animated, Action completionHandler)
        {
            NSUrlProtocol.UnregisterClass(new ObjCRuntime.Class(typeof(CoreCustomUrlProtocol)));
            base.DismissViewController(animated, completionHandler);
        }
    }
}
