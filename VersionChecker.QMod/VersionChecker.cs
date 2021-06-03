﻿#if SUBNAUTICA
using Oculus.Newtonsoft.Json;
#elif BELOWZERO
using Newtonsoft.Json;
#endif
using QModManager.API;
using SMLHelper.V2.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;
using Logger = BepInEx.Subnautica.Logger;

namespace Straitjacket.Subnautica.Mods.VersionChecker.QMod
{
    internal class VersionChecker : MonoBehaviour
    {
        internal enum CheckFrequency
        {
            Startup,
            Hourly,
            Daily,
            Weekly,
            Monthly,
            Never
        }

        private delegate string ApiKeyCommand(string apiKey = null);

        private const string VersionCheckerApiKey = "VersionCheckerApiKey";

        private static string apiKey;
        public static string ApiKey
        {
            get => apiKey ??= VersionRecord.ApiKey = PlayerPrefs.HasKey(VersionCheckerApiKey) ? PlayerPrefs.GetString(VersionCheckerApiKey) : null;
            set => apiKey = VersionRecord.ApiKey = value;
        }

        private static VersionChecker main;
        public static VersionChecker Main => main ??= new GameObject("VersionChecker").AddComponent<VersionChecker>();

        private bool isRunning = false;
        private bool startupChecked = false;
        private readonly Config Config = OptionsPanelHandler.Main.RegisterModOptions<Config>();
        private readonly Dictionary<string, VersionRecord> RecordsByQModId = new Dictionary<string, VersionRecord>();
        private readonly Dictionary<string, Color> ColorsByQModId = new Dictionary<string, Color>();

        private static readonly Color[] colors = new Color[]
        {
            new Color(0, 1, 1), new Color(.65f, .165f, .165f), new Color(0, .5f, 0), new Color(.68f, .85f, .9f), new Color(0, .5f, .5f),
            new Color(0, 1, 0), new Color(1, 0, 1), new Color(.5f, .5f, 0), new Color(1, .65f, 0), new Color(1, 1, 0)
        };

        private Color GetColor(VersionRecord record)
        {
            if (ColorsByQModId.TryGetValue(record.QModJson.Id, out Color color))
            {
                return color;
            }

            var availableColors = colors.Except(ColorsByQModId.Values);
            if (!availableColors.Any())
            {
                var colorUses = ColorsByQModId.Values
                    .GroupBy(x => x)
                    .ToDictionary(x => x.Key, x => x.Count());
                availableColors = colorUses
                    .Where(x => x.Value == colorUses.Min(x => x.Value))
                    .Select(x => x.Key)
                    .Distinct();
            }

            return ColorsByQModId[record.QModJson.Id] = availableColors.ElementAt(Random.Range(0, availableColors.Count() - 1));
        }

        public void Check(QModJson qModJson)
        {
            if (qModJson is null)
            {
                throw new ArgumentNullException("qModJson");
            }

            if (qModJson.NexusId is null && qModJson.VersionChecker is null)
            {
                return;
            }

            if (!RecordsByQModId.ContainsKey(qModJson.Id))
            {
                RecordsByQModId[qModJson.Id] = new VersionRecord(qModJson);
            }
        }

#pragma warning disable IDE0051 // Remove unused private members
        private void Awake()
#pragma warning restore IDE0051 // Remove unused private members
        {
            if (main != null && main != this)
            {
                Destroy(this);
            }
            else
            {
                main = this;
                gameObject.AddComponent<SceneCleanerPreserve>();
                DontDestroyOnLoad(this);
                SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
                SceneManager.sceneLoaded += SceneManager_sceneLoaded;

                ConsoleCommandsHandler.Main.RegisterConsoleCommand<ApiKeyCommand>("apikey", SetApiKey);
            }
        }

        private void SceneManager_sceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!isRunning)
            {
                isRunning = true;
                _ = CheckVersionsAsyncLoop();
                SceneManager.sceneLoaded -= SceneManager_sceneLoaded;
            }
        }

        private string SetApiKey(string apiKey = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                if (PlayerPrefs.HasKey(VersionCheckerApiKey))
                {
                    apiKey = PlayerPrefs.GetString(VersionCheckerApiKey);
                    return $"Nexus API key ending in {apiKey.Substring(Math.Max(0, apiKey.Length - 4))} is set.";
                }
                else
                {
                    return "Nexus API key not set.";
                }
            }
            else if (apiKey.ToLowerInvariant() == "unset")
            {
                PlayerPrefs.DeleteKey(VersionCheckerApiKey);
                return "Nexus API key unset.";
            }
            else
            {
                PlayerPrefs.SetString(VersionCheckerApiKey, ApiKey = apiKey.Trim());
                Config.LastChecked = default;
                return "Nexus API key set.";
            }
        }

        private async Task CheckVersionsAsyncLoop()
        {
            await Task.Delay(500);

            while (true)
            {
                _ = Logger.LogDebugAsync("Awaiting next check...");
                await ShouldCheckVersionsAsync();
                _ = Logger.LogDebugAsync("Time to check versions.");

                Config.LastChecked = DateTime.UtcNow;
                Config.Save();

                _ = Logger.LogInfoAsync("Initiating version checks...");

                try
                {
                    IEnumerable<Task> updateRecordTasks = RecordsByQModId.Values
                        .Except(RecordsByQModId.Values
                            .Where(x => x.QModJson.Id != "QModManager")
                            .Where(x => !QModServices.Main.FindModById(x.QModJson.Id).IsLoaded))
                        .Select(async record =>
                    {
                        try
                        {
                            await record.UpdateAsync();
                        }
                        catch (WebException e)
                        {
                            _ = Logger.LogDebugAsync($"{record.Prefix}There was an error retrieving the latest version: " +
                                $"Could not connect to address.{Environment.NewLine}" + e.Message + Environment.NewLine);
                        }
                        catch (JsonReaderException e)
                        {
                            _ = Logger.LogDebugAsync($"{record.Prefix}There was an error retrieving the latest version: " +
                                $"Invalid JSON response.{Environment.NewLine}" + e.Message + Environment.NewLine);
                        }
                        catch (JsonSerializationException e)
                        {
                            _ = Logger.LogDebugAsync($"{record.Prefix}There was an error retrieving the latest version: " +
                                Environment.NewLine + e.Message + Environment.NewLine);
                        }
                        catch (InvalidOperationException e)
                        {
                            _ = Logger.LogDebugAsync($"{record.Prefix}There was an error retrieving the latest version: " +
                                Environment.NewLine + e.Message + Environment.NewLine);
                        }
                        catch (Exception e)
                        {
                            _ = Logger.LogErrorAsync($"{record.Prefix}There was an error retrieving the latest version: " +
                                Environment.NewLine + e.ToString() + Environment.NewLine);
                        }
                    });
                    await Task.WhenAll(updateRecordTasks);
                }
                catch (Exception e)
                {
                    _ = Logger.LogErrorAsync(e.ToString() + Environment.NewLine);
                }
                finally
                {
                    _ = Logger.LogInfoAsync("Version checks complete.");

                    _ = PrintOutdatedVersionsAsync();
                }
            }
        }

        private async Task PrintOutdatedVersionsAsync()
        {
            await NoWaitScreenAsync();
            await Task.Delay(1000);

            foreach (var record in RecordsByQModId.Values)
            {
                _ = PrintVersionInfoAsync(record);
            }
        }

        private async Task PrintVersionInfoAsync(VersionRecord record)
        {
            if (record.QModJson.NexusId is null && record.QModJson.VersionChecker is null)
            {
                return;
            }

            switch (record.State)
            {
                case VersionRecord.VersionState.Outdated:
                    _ = Logger.LogWarningAsync($"{record.Prefix}{record.Message(false)}");

                    for (var i = 0; i < 3; i++)
                    {
                        _ = Logger.DisplayMessageAsync($"[<color=#{ColorUtility.ToHtmlStringRGB(GetColor(record))}>" +
                            $"{record.QModJson.DisplayName}</color>] {record.Message(true)}");

                        if (i < 2)
                            await Task.WhenAll(Task.Delay(5000), NoWaitScreenAsync());
                    }
                    break;
                case VersionRecord.VersionState.Unknown:
                    _ = Logger.LogWarningAsync($"{record.Prefix}{record.Message(false)}");
                    break;
                case VersionRecord.VersionState.Ahead:
                case VersionRecord.VersionState.Current:
                default:
                    _ = Logger.LogMessageAsync($"{record.Prefix}{record.Message(false)}");
                    break;
            }
        }

        private async Task ShouldCheckVersionsAsync()
        {
            if (Config.Frequency == CheckFrequency.Startup && !startupChecked)
            {
                startupChecked = true;
                return;
            }

            bool shouldCheck = false;

            while (!shouldCheck)
            {
                switch (Config.Frequency)
                {
                    default:
                    case CheckFrequency.Never:
                        shouldCheck = false;
                        break;
                    case CheckFrequency.Hourly:
                        await SpecificDateTimeAsync(Config.LastChecked.AddHours(1), 60000);
                        shouldCheck = true;
                        break;
                    case CheckFrequency.Daily:
                        await SpecificDateTimeAsync(Config.LastChecked.AddDays(1), 3600000);
                        shouldCheck = true;
                        break;
                    case CheckFrequency.Weekly:
                        await SpecificDateTimeAsync(Config.LastChecked.AddDays(7), 3600000);
                        shouldCheck = true;
                        break;
                    case CheckFrequency.Monthly:
                        await SpecificDateTimeAsync(Config.LastChecked.AddMonths(1), 3600000);
                        shouldCheck = true;
                        break;
                }

                if (!shouldCheck)
                    await Task.Delay(60000);
            }
        }
        private async Task SpecificDateTimeAsync(DateTime dateTime, int millisecondsInterval = 1000)
        {
            while (DateTime.UtcNow < dateTime)
                await Task.Delay(millisecondsInterval);
        }

        private async Task NoWaitScreenAsync()
        {
            while (WaitScreen.main?.isShown ?? false)
                await Task.Delay(1000);
        }
    }
}
