﻿/*
This source file is subject to version 3 of the GPL license, 
that is bundled with this package in the file LICENSE, and is 
available online at http://www.gnu.org/licenses/gpl.txt; 
you may not use this file except in compliance with the License. 

Software distributed under the License is distributed on an "AS IS" basis,
WITHOUT WARRANTY OF ANY KIND, either express or implied. See the License for
the specific language governing rights and limitations under the License.

All portions of the code written by Voat are Copyright (c) 2014 Voat
All Rights Reserved.

This source file is subject to The Code Project Open License (CPOL) 1.02
Original code can be found at: http://rionscode.wordpress.com/2013/02/24/prevent-repeated-requests-using-actionfilters-in-asp-net-mvc/
Copyright Rion Williams 2013
*/

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Mvc;
using Voat.Models;
using Newtonsoft.Json;

namespace Voat.Utils
{
    public class PreventSpamAttribute : ActionFilterAttribute
    {
        // This stores the time between Requests (in seconds)
        public int DelayRequest = 10;
        
        // The Error Message that will be displayed in case of excessive Requests
        public string ErrorMessage = "Excessive Request Attempts Detected.";
        
        // This will store the URL to Redirect errors to
        public string RedirectURL;

        private bool trustedUser = false;

        public override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            var loggedInUser = filterContext.HttpContext.User.Identity.Name;

            // user is submitting a message
            if (filterContext.ActionParameters.ContainsKey("message"))
            {
                Message incomingMessage = (Message)filterContext.ActionParameters["message"];
                var targetSubverse = incomingMessage.Subverse;

                // check user LCP for target subverse
                if (targetSubverse != null)
                {
                    var LCPForSubverse = Karma.LinkKarmaForSubverse(loggedInUser, targetSubverse);
                    if (LCPForSubverse >= 40)
                    {
                        // lower DelayRequest time
                        DelayRequest = 10;
                    }
                    else if (User.IsUserSubverseModerator(loggedInUser, targetSubverse))
                    {
                        // lower DelayRequest time
                        DelayRequest = 10;
                    }
                }
            }
            // user is submitting a comment
            else if (filterContext.ActionParameters.ContainsKey("comment"))
            {
                Comment incomingComment = (Comment)filterContext.ActionParameters["comment"];

                using (whoaverseEntities db = new whoaverseEntities())
                {
                    var relatedMessage = db.Messages.Find(incomingComment.MessageId);
                    if (relatedMessage != null)
                    {
                        var targetSubverseName = relatedMessage.Subverse;

                        // check user CCP for target subverse
                        int CCPForSubverse = Karma.CommentKarmaForSubverse(loggedInUser, targetSubverseName);
                        if (CCPForSubverse >= 40)
                        {
                            // lower DelayRequest time
                            DelayRequest = 10;
                        }
                        else if (User.IsUserSubverseModerator(loggedInUser, targetSubverseName))
                        {
                            // lower DelayRequest time
                            DelayRequest = 10;
                        }
                    }
                }
            }

            // Store our HttpContext (for easier reference and code brevity)
            var request = filterContext.HttpContext.Request;

            // Store our HttpContext.Cache (for easier reference and code brevity)
            var cache = filterContext.HttpContext.Cache;

            // Grab the IP Address from the originating Request (very simple implementation for example purposes)
            var originationInfo = request.ServerVariables["HTTP_X_FORWARDED_FOR"] ?? request.UserHostAddress;

            // Append the User Agent
            originationInfo += request.UserAgent;

            // Now we just need the target URL Information
            var targetInfo = request.RawUrl + request.QueryString;

            // Generate a hash for your strings (this appends each of the bytes of the value into a single hashed string
            var hashValue = string.Join("", MD5.Create().ComputeHash(Encoding.ASCII.GetBytes(originationInfo + targetInfo)).Select(s => s.ToString("x2")));

            // TODO:
            // Override spam filter if user is authorized poster to target subverse
            // trustedUser = true;

            // Checks if the hashed value is contained in the Cache (indicating a repeat request)
            if (cache[hashValue] != null && loggedInUser != "system" && trustedUser != true)
            {
                // Adds the Error Message to the Model and Redirect
                filterContext.Controller.ViewData.ModelState.AddModelError(string.Empty, ErrorMessage);
            }
            else
            {
                // Adds an empty object to the cache using the hashValue to a key (This sets the expiration that will determine
                // if the Request is valid or not
                cache.Add(hashValue, "", null, DateTime.Now.AddSeconds(DelayRequest), Cache.NoSlidingExpiration, CacheItemPriority.Default, null);
            }

            base.OnActionExecuting(filterContext);
        }
    }

    public static class ReCaptchaUtility
    {
        public static string Validate(string encodedResponse)
        {
            var client = new WebClient();
            string privateKey = ConfigurationManager.AppSettings["recaptchaPrivateKey"];
            var googleReply = client.DownloadString(string.Format("https://www.google.com/recaptcha/api/siteverify?secret={0}&response={1}", privateKey, encodedResponse));
            var captchaResponse = JsonConvert.DeserializeObject<ReCaptchaResponseClass>(googleReply);
            return captchaResponse.Success;
        }
    }

    public class ReCaptchaResponseClass
    {
        [JsonProperty("success")]
        public string Success { get; set; }

        [JsonProperty("error-codes")]
        public List<string> ErrorCodes { get; set; }
    }
}