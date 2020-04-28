﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using MonkeyCache;
using MonkeyCache.FileStore;
using Newtonsoft.Json;
using TurnipTracker.Model;
using TurnipTracker.Services;
using TurnipTracker.Shared;
using Xamarin.Essentials;
using Xamarin.Forms;

[assembly: Dependency(typeof(DataService))]
namespace TurnipTracker.Services
{
    public class HttpResponseException : Exception
    {
        public HttpStatusCode StatusCode { get; private set; }

        public HttpResponseException(HttpStatusCode statusCode, string content) : base(content)
        {
            StatusCode = statusCode;
        }
    }
    public class DataService
    {
        IBarrel barrel;
        object locker = new object();
        CalendarWeekRule myCWR;
        DayOfWeek myFirstDOW;
        CultureInfo myCI;

        readonly HttpClient client;

        public  DataService()
        {
            client = new HttpClient()
            {
                BaseAddress = new Uri(App.BaseUrl)
            };

            Barrel.ApplicationId = AppInfo.PackageName;
            barrel = Barrel.Create(FileSystem.AppDataDirectory);
            myCI = new CultureInfo("en-US");
            myCWR = myCI.DateTimeFormat.CalendarWeekRule;
            myFirstDOW = myCI.DateTimeFormat.FirstDayOfWeek;
        }

        public Profile GetProfile()
        {
            lock (locker)
            {
                var profile = barrel.Get<Profile>("profile");
                return profile ?? new Profile
                {
                    Fruit = (int)Model.Fruit.Apple,
                    Status = "😍"
                };
            }
        }

        public void SaveProfile(Profile profile)
        {
            lock(locker)
            {
                barrel.Add<Profile>("profile", profile, TimeSpan.FromDays(1));
            }
        }

        int GetWeekOfYear() =>
            myCI.Calendar.GetWeekOfYear(DateTime.Now, myCWR, myFirstDOW);

        string GetCurrentWeekKey() =>
            $"week_{GetWeekOfYear()}_{DateTime.Now.Year}";

        public List<Day> GetCurrentWeek()
        {
            lock (locker)
            {
                var days = barrel.Get<List<Day>>(GetCurrentWeekKey());

                return days ?? new List<Day>
                {
                    new Day { DayLong = "Sunday", IsSunday = true},
                    new Day { DayLong = "Monday" },
                    new Day { DayLong = "Tuesday" },
                    new Day { DayLong = "Wednesday" },
                    new Day { DayLong = "Thursday" },
                    new Day { DayLong = "Friday" },
                    new Day { DayLong = "Saturday" }
                };
            }
        }

        public async Task UpsertUserProfile(Profile profile)
        {
            profile ??= GetProfile();
  
            var user = new User
            { 
                Fruit = profile.Fruit,
                IslandName = profile.IslandName,
                Name = profile.Name,
                Status = profile.Status ?? string.Empty,
                TimeZone = profile.TimeZone ?? string.Empty
            };
            user.PublicKey = await SettingsService.GetPublicKey();
            if (SettingsService.HasRegistered)
            {
                var content = JsonConvert.SerializeObject(user);
                await PutAsync($"api/UpdateProfile?code={App.PutUpdateProfileKey}", content);
            }
            else
            {                
                var content = JsonConvert.SerializeObject(user);
                await PostAsync($"api/CreateProfile?code={App.PostCreateProfileKey}", content);
                SettingsService.HasRegistered = true;
            }
        }

        public async Task UpdateTurnipPrices(Day day)
        {
            day ??= GetCurrentWeek()[(int)DateTime.Now.DayOfWeek];
            var key = await SettingsService.GetPublicKey();
            var turnipUpdate = new TurnipUpdate
            {
                AMPrice = day.PriceAM ?? 0,
                PMPrice = day.PricePM ?? 0,
                BuyPrice = day.BuyPrice ?? 0,
                Year = DateTime.Now.Year,
                DayOfYear = DateTime.Now.DayOfYear,
                PublicKey = key,
                TurnipUpdateTimeUTC = DateTime.UtcNow
            };

            var content = JsonConvert.SerializeObject(turnipUpdate);

            
            await PutAsync($"api/UpdateTurnipPrices?code={App.PutUpdateTurnipPricesKey}", content);
        }

        public async Task SubmitFriendRequestAsync(string requesterKey)
        {
            var publicKey = await SettingsService.GetPublicKey();
            var content = JsonConvert.SerializeObject(new FriendRequest { MyPublicKey = publicKey, FriendPublicKey = requesterKey });


            await PostAsync($"api/SubmitFriendRequest?code={App.PostSubmitFriendRequestKey}", content);
        }

        public async Task RemoveFriendAsync(string friendToRemoveKey)
        {
            var publicKey = await SettingsService.GetPublicKey();
            await DeleteAsync($"api/RemoveFriend/{publicKey}/{friendToRemoveKey}?code={App.DeleteRemoveFriendKey}");
        }

        public async Task RemoveFriendRequestAsync(string friendRequestToRemoveKey)
        {
            var publicKey = await SettingsService.GetPublicKey();
            var content = JsonConvert.SerializeObject(new FriendRequest { MyPublicKey = publicKey, FriendPublicKey = friendRequestToRemoveKey });
            await PostAsync($"api/RejectFriendRequest?code={App.PostRemoveFriendRequestKey}", content);
        }

        public async Task ApproveFriendRequestAsync(string friendToApproveKey)
        {
            var publicKey = await SettingsService.GetPublicKey();
            var content = JsonConvert.SerializeObject(new FriendRequest { MyPublicKey = publicKey, FriendPublicKey = friendToApproveKey });
            await PostAsync($"api/ApproveFriendRequest?code={App.PostApproveFriendRequestKey}", content);
        }

        public async Task<IEnumerable<FriendStatus>> GetFriendsAsync(bool forceRefresh = false)
        {
            var publicKey = await SettingsService.GetPublicKey();

            return await GetAsync<IEnumerable<FriendStatus>>($"api/GetFriends/{publicKey}?code={App.GetFriendsKey}", "get_friends", 5, forceRefresh);
        }

        public async Task<IEnumerable<PendingFriendRequest>> GetFriendRequestsAsync(bool forceRefresh = false)
        {
            var publicKey = await SettingsService.GetPublicKey();

            return await GetAsync<IEnumerable<PendingFriendRequest>>($"api/GetFriendRequests/{publicKey}?code={App.GetFriendRequestsKey}", "get_friend_requests", 5, forceRefresh);
        }
        public void SaveCurrentWeek(List<Day> days)
        {
            lock(locker)
            {
                barrel.Add<List<Day>>(GetCurrentWeekKey(), days, TimeSpan.FromDays(7));
            }
        }

        async Task SetHeader()
        {
            if (client.DefaultRequestHeaders.Authorization == null)
            {
                var key = await SettingsService.GetPrivateKey();
                var encoding = Encoding.GetEncoding("iso-8859-1");
                var authenticationBytes = encoding.GetBytes(key);
                var token = Convert.ToBase64String(authenticationBytes);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        async Task DeleteAsync(string url)
        {
            await SetHeader();
            
            using var response = await client.DeleteAsync(url);

            using var responseContent = response.Content;
            var responseString = await responseContent.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpResponseException(response.StatusCode, responseString);
            }
        }

        async Task PutAsync(string url, string content)
        {
            await SetHeader();

            using var response = await client.PutAsync(url, new StringContent(content, Encoding.UTF8, "application/json"));

            using var responseContent = response.Content;
            var responseString = await responseContent.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpResponseException(response.StatusCode, responseString);
            }
        }

        async Task PostAsync(string url, string content)
        {
            await SetHeader();

            using var response = await client.PostAsync(url, new StringContent(content, Encoding.UTF8, "application/json"));

            using var responseContent = response.Content;
            var responseString = await responseContent.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpResponseException(response.StatusCode, responseString);
            }
        }

        async Task<T> PostAsync<T>(string url, string content)
        {
            await SetHeader();

            using var response = await client.PostAsync(url, new StringContent(content, Encoding.UTF8, "application/json"));

            using var responseContent = response.Content;
            var responseString = await responseContent.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpResponseException(response.StatusCode, responseString);
            }

            return JsonConvert.DeserializeObject<T>(responseString);
        }

        async Task<T> GetAsync<T>(string url, string key, int mins = 7, bool forceRefresh = false)
        {
            var json = string.Empty;

            if (Connectivity.NetworkAccess != NetworkAccess.Internet)
                json = Barrel.Current.Get<string>(key);
            else if (!forceRefresh && !Barrel.Current.IsExpired(key))
                json = Barrel.Current.Get<string>(key);

            try
            {
                if (string.IsNullOrWhiteSpace(json))
                {
                    await SetHeader();
                    
                    json = await client.GetStringAsync(url);
 
                    Barrel.Current.Add(key, json, TimeSpan.FromMinutes(mins));
                }
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to get information from server {ex}");
                throw ex;
            }
        }
    }
}
