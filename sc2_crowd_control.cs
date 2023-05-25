// based on https://github.com/DerMitDemRolfTanzt/fs22-twitchevents/blob/master/crowdcontrol/src/fs22effectpack.cs
/*
MIT License

Copyright (c) 2022 DerMitDemRolfTanzt

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using ConnectorLib;
using CrowdControl.Common;
using ConnectorType = CrowdControl.Common.ConnectorType;
using Log = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs;

public enum Method
{
    // not sure if this enum is needed
    StartEffect,
    StopEffect,
}

public class SC2Randomizer : PCEffectPack<NullConnector>
{
    // First argument is an effect pack index assigned internally by Warp World for official effect packs.
    // We can use any integer here since it's ignored for any SDK/ccpak plugin.
    public override Game Game { get; } = new(132, "StarCraft 2 Randomizer", "SC2Randomizer", "PC", ConnectorType.NullConnector);

    public SC2Randomizer(UserRecord player, Func<CrowdControlBlock, bool> responseHandler, Action<object> statusUpdateHandler)
        : base(player, responseHandler, statusUpdateHandler)
    {
    }

    // Unfortunately the XML Assembly is not embedded to the CrowdControl SDK, therefore we need to write and parse XML manually.

    protected bool XmlWrite(EffectRequest request, Method method)
    {
        string parameterItems = "";
        if (request.Parameters != null)
        {
            parameterItems = String.Join(",", request.Parameters);
        }

        string eventsXml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Bank version=""1"">
    <Section name=""header"">
        <Key name=""version"">
            <Value string=""{version}""/>
        </Key>
        <Key name=""date"">
            <Value string=""{request.Stamp}""/>
        </Key>
        <Key name=""ticker"">
            <Value int=""{DateTime.Now.Ticks}""/>
        </Key>
    </Section>
    <Section name=""request"">
        <Key name=""code"">
            <Value string=""{request.EffectID}""/>
        </Key>
        <Key name=""FinalCode"">
            <Value string=""{FinalCode(request)}""/>
        </Key>
        <Key name=""method"">
            <Value string=""{method}""/>
        </Key>
        <Key name=""DisplayViewer"">
            <Value string=""{request.DisplayViewer}""/>
        </Key>
        <Key name=""id"">
            <Value string=""{request.ID}""/>
        </Key>
        <Key name=""params"">
            <Value string=""{parameterItems}""/>
        </Key>
        <Key name=""quantity"">
            <Value string=""{request.Quantity}""/>
        </Key>
        <Key name=""duration"">
            <Value string=""{request.Duration.TotalSeconds}""/>
        </Key>
    </Section>
</Bank>
";

        File.WriteAllText(xmlPathRequests, eventsXml);
        return true;
    }

    protected string GetXmlSection(string xml, string section)
    {
        try
        {
            var match = Regex.Match(xml,
                "<Bank version=\".*?\">.*"
                + "<Section name=\"" + section + "\">(.*?)</Section>.*</Bank>",
                RegexOptions.Singleline);

            if (!match.Success) return null;
            return match.Groups[1].Value;
        }
        catch (Exception e)
        {
            Log.Message(e.ToString());
            return null;
        }
    }

    protected string GetXmlString(string section, string key)
    {
        try
        {
            var match = Regex.Match(section,
                "<Key name=\"" + key + "\">\\s*"
                + "<Value string=\"([^\"]+)\"/>",
                RegexOptions.Singleline);

            if (!match.Success) return null;
            return match.Groups[1].Value;
        }
        catch (Exception e)
        {
            Log.Message(e.ToString());
            return null;
        }
    }

    protected string XmlCheckStatus(EffectRequest request, Method method)
    {
        if (!File.Exists(xmlPathResponses))
        {
            return "fail";
        }

        string data = File.ReadAllText(xmlPathResponses);
        data = GetXmlSection(data, "responses");
        return GetXmlString(data, request.ID.ToString());
    }

    protected string GetGameStatus()
    {
        try
        {
            if (xmlPathResponses == "")
            {
                fileStatus = "fail";
                return fileStatus;
            }
            if (!File.Exists(xmlPathResponses))
            {
                fileStatus = "fail";
                return fileStatus;
            }
            var dict = ParseXml(xmlPathResponses);
            DateTime t = DateTime.Parse(dict["date"]);
            if (t < DateTime.Now.AddHours(-24))
            {
                fileStatus = "expired";
                return fileStatus;
            }
            fileStatus = dict["status"];
            return fileStatus;
        }
        catch (Exception e)
        {
            Log.Message("error in GetGameStatus() with " + xmlPathResponses + ": " + e.ToString());
        }
        fileStatus = "fail";
        return fileStatus;
    }

    protected Dictionary<string, string> ParseXml(string file)
    {
        var dict = new Dictionary<string, string>();
        string data = File.ReadAllText(file);
        data = GetXmlSection(data, "header");
        dict["date"] = GetXmlString(data, "date");
        dict["status"] = GetXmlString(data, "status");
        return dict;
    }

    protected bool FindXmlInPath(string root)
    {
        string[] files = Directory.GetFiles(root, "CrowdControlResponses.SC2Bank", SearchOption.AllDirectories);
        string newest_file = "";
        string newest_status = "";
        DateTime newest = new DateTime(0);

        foreach (string file in files)
        {
            try
            {
                var dict = ParseXml(file);
                DateTime t = DateTime.Parse(dict["date"]);
                Log.Message($"{file} status: {dict["status"]}, date: {t}");

                // should it care about status starting?
                if (t > newest && dict["status"] != "exited")
                {
                    newest = t;
                    newest_file = file;
                    newest_status = dict["status"];
                }
            }
            catch (Exception e)
            {
                Log.Message("error with " + file + ": " + e.ToString());
            }
        }

        // ignore files older than 24 hours
        if (newest > DateTime.Now.AddHours(-24))
        {
            Log.Message($"found file {newest_file} with date: {newest}, status: {newest_status}");
            fileStatus = newest_status;
            xmlPathResponses = newest_file;
            xmlPathRequests = xmlPathResponses.Replace("CrowdControlResponses.SC2Bank", "CrowdControl.SC2Bank");
            return true;
        }
        return false;
    }

    protected bool FindXml()
    {
        // we can use the status and date in the file to determine which one to use
        // we could also make the mod delete the bank instead of setting the status to exited?
        // search in the Accounts folder first, then the test folder if we don't find it
        Log.Message("FindXml");
        // check normal paths
        try
        {
            string root = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StarCraft II/Accounts");
            if (FindXmlInPath(root)) return true;
        }
        catch (Exception e)
        {
            Log.Message("error with searching user path: " + e.ToString());
        }

        try
        {
            string onedrive = (string)Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\OneDrive", "UserFolder", "");
            string root = Path.Join(onedrive, "Documents/StarCraft II/Accounts");
            if (FindXmlInPath(root)) return true;
        }
        catch (Exception e)
        {
            Log.Message("error with searching onedrive path: " + e.ToString());
        }

        // check dev paths...
        try
        {
            string root = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StarCraft II/Banks");
            if (FindXmlInPath(root)) return true;
        }
        catch (Exception e)
        {
            Log.Message("error with searching dev path: " + e.ToString());
        }

        try
        {
            string onedrive = (string)Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\OneDrive", "UserFolder", "");
            string root = Path.Join(onedrive, "Documents/StarCraft II/Banks");
            if (FindXmlInPath(root)) return true;
        }
        catch (Exception e)
        {
            Log.Message("error with searching dev onedrive path: " + e.ToString());
        }

        /* I hope I never need this...
        EnumerationOptions e = new EnumerationOptions();
        e.IgnoreInaccessible = true;
        e.RecurseSubdirectories = true;
        e.MatchCasing = MatchCasing.CaseSensitive;
        string[] gamePaths = Directory.GetDirectories(@"c:\", "StarCraft II", e);
        Log.Message("gamePaths length == " + gamePaths.Length.ToString());
        foreach (string gamePath in gamePaths) {
            Log.Message(gamePath);
        }*/

        return false;
    }

    protected bool XmlWait(EffectRequest request, Method method, int millisecondsTimeout = 1000, int millisecondsCheckInterval = 100)
    {
        // waiting in here seems to block up the queue of other incoming effects
        string status = null;
        SpinWait.SpinUntil(() =>
        {
            Thread.Sleep(millisecondsCheckInterval);
            status = XmlCheckStatus(request, method);
            return status is not null;
        }, millisecondsTimeout);

        Log.Message($"XmlWait got status {status}");
        if (status is null)
        {
            DelayEffect(request);
            return false;
        }
        else if (status == "retry")
        {
            DelayEffect(request);
            return false;
        }
        else if (status != "success")
        {
            return false;
        }
        return true;
    }

    protected bool SendEffect(EffectRequest request, Method method)
    {
        // don't think I need FindXml() here since we already call it in IsReady()
        XmlWrite(request, method);
        bool success = XmlWait(request, method);
        return success;
    }

    #region Effect List
    public override EffectList Effects
    {
        get
        {
            List<Effect> result = new List<Effect>
            {
                new("Musical Chairs", "musicalchairs"),
                new("Black Sheep Wall", "fullvision") { Duration = 60 },
                new("Terrible, Terrible Damage", "extradamage") { Duration = 60 },
                new("Reduced Damage", "reduceddamage") { Duration = 60 },

                new("Slow Game Speed", "slowspeed") { Duration = 60 },
                new("Super Game Speed", "superspeed") { Duration = 60 },
                new("Max Upgrades", "maxupgrades"),
                new("Reset Upgrades", "resetupgrades"),
                new("Set Upgrades", "setupgrades") { Quantity = 3 },

                ///new("Mean Things That Kill", "mean", ItemKind.Folder),
                new("Nuke All Town Halls", "nukes") { Category = "Rude Things" },
                new("Kill All Workers", "killworkers") { Category = "Rude Things" },
                new("Kill All Army", "killarmy") { Category = "Rude Things" },

                ///new("Resources", "resources", ItemKind.Folder),
                new("Give Minerals (x1000)", "giveminerals") { Category = "Resources", Quantity = 100 },
                new("Give Gas (x1000)", "givegas") { Category = "Resources", Quantity = 100 },
                new("Take Minerals (x1000)", "takeminerals") { Category = "Resources", Quantity = 100 },
                new("Take Gas (x1000)", "takegas") { Category = "Resources", Quantity = 100 },
                new("Raise Supply Limit", "raisesupply") { Category = "Resources", Quantity = 50 },
                new("Lower Supply Limit", "lowersupply") { Category = "Resources", Quantity = 50  },
            };
            return result;
        }
    }

    #endregion

    protected override bool IsReady(EffectRequest request)
    {
        if (lastSearch > DateTime.Now.AddSeconds(-30))
        {
            // this lazy evaluation might cause issues if the game crashes, so it needs a timer
            if (GetGameStatus() == "playing") return true;
        }
        lastSearch = DateTime.Now;
        return FindXml() && fileStatus == "playing";
    }

    protected override void StartEffect(EffectRequest request)
    {
        if (!IsReady(request))
        {
            Log.Message("not ready yet");
            Respond(request, EffectStatus.FailTemporary, "Not ready yet");
            return;
        }

        Effect? effect = null;
        TryEffect(request,
            () => Effects.TryGetValue(request.EffectID, out effect), // "condition"
            () => // action
            {
                try
                {
                    return SendEffect(request, Method.StartEffect);
                }
                catch (Exception e)
                {
                    Log.Message("error with SendEffect: " + e.ToString());
                    return false;
                }
            },
            () => Connector.SendMessage($"{request.DisplayViewer} invoked {effect.Name}."), // followUp
            null, false, FinalCode(request)
        ); // TimeSpan retryDelay, bool retryOnFail, string mutex name, TimeSpan? holdMutex = null
    }

    protected override bool StopEffect(EffectRequest request)
    {
        return true;
    }

    protected override void RequestData(DataRequest request) => Respond(request, request.Key, null, false, $"Variable name \"{request.Key}\" not known.");

    const bool debug = false;

    const string version = "1.2";

    string xmlPathRequests = "";
    string xmlPathResponses = "";
    string fileStatus = "";
    DateTime lastSearch = new DateTime(0);
}