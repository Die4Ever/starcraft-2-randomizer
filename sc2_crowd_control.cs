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
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using ConnectorLib;
using CrowdControl.Common;
using JetBrains.Annotations;
using ConnectorType = CrowdControl.Common.ConnectorType;
using Log = CrowdControl.Common.Log;

namespace CrowdControl.Games.Packs
{
    public enum Method {
        StartEffect,
        StopEffect,
    }

    public class SC2EffectPack : PCEffectPack<NullConnector>
    {
        // First argument is an effect pack index assigned internally by Warp World for official effect packs.
        // We can use any integer here since it's ignored for any SDK/ccpak plugin.
        public override Game Game { get; } = new Game(99999, "StarCraft 2 Randomizer", "SC2EffectPack", "PC", ConnectorType.NullConnector);

        public SC2EffectPack([NotNull] IPlayer player, [NotNull] Func<CrowdControlBlock, bool> responseHandler, [NotNull] Action<object> statusUpdateHandler)
            : base(player, responseHandler, statusUpdateHandler)
        {
        }

        #region debug

        protected string GetFields(Type t, string separator = "", BindingFlags flags = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly) {
            return String.Join(separator, t.GetFields(flags).ToList().Select(field => $"<Field>{field}</Field>"));
        }

        protected string GetProperties(Type t, string separator = "", BindingFlags flags = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly) {
            return String.Join(separator, t.GetProperties(flags).ToList().Select(property => $"<Property>{property}</Property>"));
        }

        protected string GetMethods(Type t, string separator = "", BindingFlags flags = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly) {
            return String.Join(separator, t.GetMethods(flags).ToList().Select(method => $"<Method>{method}</Method>"));
        }

        protected string GetConstructors(Type t, string separator = "", BindingFlags flags = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly) {
            return String.Join(separator, t.GetConstructors(flags).ToList().Select(constructor => $"<Constructor>{constructor}</Constructor>"));
        }

        protected string GetConnectorTypes() {
            var q = AppDomain.CurrentDomain.GetAssemblies()
                       .SelectMany(t => t.GetTypes())
                       .Where(t => t.Name.Contains("EffectPack"));
            return String.Join("\n        ", q.ToList().Select( c => $"<ConnectorType><Namespace>{c.Namespace}</Namespace><Name>{c.Name}</Name><BaseType>{c.BaseType}</BaseType><Fields>{GetFields(c)}</Fields><Properties>{GetProperties(c)}</Properties><Methods>{GetMethods(c)}</Methods><Constructors>{GetConstructors(c)}</Constructors></ConnectorType>" ));
        }

        #endregion

        // Unfortunately the XML Assembly is not embedded to the CrowdControl SDK, therefore we need to write and parse XML manually.

        protected bool XmlWrite(EffectRequest request, Method method) {
            string parameterItems = String.Join(",", request.ParameterItems.Select(i => $"{i.AsSimpleType}"));

            string eventsXml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Bank version=""1"">
    <Section name=""header"">
        <Key name=""version"">
            <Value string=""{version}""/>
        </Key>
        <Key name=""date"">
            <Value string=""{request.Stamp}""/>
        </Key>
    </Section>
    <Section name=""request"">
        <Key name=""code"">
            <Value string=""{request.BaseCode}""/>
        </Key>
        <Key name=""FinalCode"">
            <Value string=""{request.FinalCode}""/>
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
    </Section>
</Bank>
";

            File.WriteAllText(xmlPathRequests, eventsXml);

            return true;
        }

        protected string XmlCheckStatus(EffectRequest request, Method method) {
            if (!File.Exists(xmlPathResponses)) {
                return "fail";
            }

            string data = File.ReadAllText(xmlPathResponses);
            data = GetXmlSection(data, "responses");
            return GetXmlString(data, request.ID.ToString());
        }

        protected string GetXmlSection(string xml, string section) {
            try {
                var match = Regex.Match(xml,
                        "<Bank version=\".*?\">.*"
                        + "<Section name=\""+section+"\">(.*?)</Section>.*</Bank>",
                        RegexOptions.Singleline);
                
                if (!match.Success) return null;
                return match.Groups[1].Value;
            } catch(Exception e) {
                Log.Message(e.ToString());
                return null;
            }
        }

        protected string GetXmlString(string section, string key) {
            try {
                var match = Regex.Match(section,
                    "<Key name=\""+key+"\">\\s*"
                    + "<Value string=\"([^\"]+)\"/>",
                    RegexOptions.Singleline);
                
                if (!match.Success) return null;
                return match.Groups[1].Value;
            } catch(Exception e) {
                Log.Message(e.ToString());
                return null;
            }
        }

        protected Dictionary<string, string> ParseXml(string file) {
            var dict = new Dictionary<string, string>();
            string data = File.ReadAllText(file);
            data = GetXmlSection(data, "header");
            dict["date"] = GetXmlString(data, "date");
            dict["status"] = GetXmlString(data, "status");
            return dict;
        }

        protected bool FindXmlInPath(string root) {
            string[] files = Directory.GetFiles(root, "CrowdControlResponses.SC2Bank", SearchOption.AllDirectories);
            string newest_file = "";
            string newest_status = "";
            DateTime newest = new DateTime(0);

            foreach (string file in files) {
                try {
                    var dict = ParseXml(file);
                    DateTime t = DateTime.Parse(dict["date"]);
                    Log.Message($"{file} status: {dict["status"]}, date: {t}");

                    // should it care about status starting?
                    if( t > newest && dict["status"] != "exited") {
                        newest = t;
                        newest_file = file;
                        newest_status = dict["status"];
                    }
                } catch(Exception e) {
                    Log.Message("error with "+file+": "+ e.ToString());
                }
            }

            // ignore files older than 24 hours
            if( newest > DateTime.Now.AddHours(-24) ) {
                Log.Message($"found file {newest_file} with date: {newest}, status: {newest_status}");
                fileStatus = newest_status;
                xmlPathResponses = newest_file;
                xmlPathRequests = xmlPathResponses.Replace("CrowdControlResponses.SC2Bank", "CrowdControl.SC2Bank");
                return true;
            }
            return false;
        }

        protected bool FindXml() {
            // we can use the status and date in the file to determine which one to use
            // we could also make the mod delete the bank instead of setting the status to exited?
            // search in the Accounts folder first, then the test folder if we don't find it
            Log.Message("FindXml");
            try {
                string root = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StarCraft II/Accounts");
                if(FindXmlInPath(root)) return true;
            } catch(Exception e) {
                Log.Message("error with searching user path: "+ e.ToString());
            }

            try {
                string root = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StarCraft II/Banks");
                return FindXmlInPath(root);
            } catch(Exception e) {
                Log.Message("error with searching dev path: "+ e.ToString());
                return false;
            }
        }

        protected bool XmlWait(EffectRequest request, Method method, int millisecondsTimeout = 5000, int millisecondsCheckInterval = 100) {
            string status = null;
            SpinWait.SpinUntil(() => {
                Thread.Sleep(millisecondsCheckInterval);
                status = XmlCheckStatus(request, method);
                return status is not null;
            }, millisecondsTimeout);

            Log.Message($"XmlWait got status {status}");
            return status is not null && status == "success";
        }

        protected bool SendEffect(EffectRequest request, Method method) {
            // don't think I need FindXml() here since we already call it in IsReady()
            XmlWrite(request, method);
            bool success = XmlWait(request, method);
            return success;
        }

        #region Effect List
        public override List<Effect> Effects
        {
            get
            {
                List<Effect> result = new List<Effect>
                {
                    new Effect("Musical Chairs", "musicalchairs"),
                    new Effect("Black Sheep Wall (1 min)", "fullvision"),
                    new Effect("Terrible, Terrible Damage (1 min)", "extradamage"),
                    new Effect("Reduced Damage (1 min)", "reduceddamage"),
                    
                    new Effect("Slow Game Speed (1 min)", "slowspeed"),
                    new Effect("Super Game Speed (1 min)", "superspeed"),
                    new Effect("Max Upgrades", "maxupgrades"),
                    new Effect("Reset Upgrades", "resetupgrades"),
                    new Effect("Set Upgrades", "setupgrades", new[]{"upgrades"}),
                    
                    new Effect("Mean Things That Kill", "mean", ItemKind.Folder),
                    new Effect("Nuke All Town Halls", "nukes", "mean"),
                    new Effect("Kill All Workers", "killworkers", "mean"),
                    new Effect("Kill All Army", "killarmy", "mean"),
                    
                    new Effect("Resources", "resources", ItemKind.Folder),
                    new Effect("Give Minerals", "giveminerals", new[]{"minerals"}, "resources"),
                    new Effect("Give Gas", "givegas", new[]{"gas"}, "resources"),
                    new Effect("Take Minerals", "takeminerals", new[]{"minerals"}, "resources"),
                    new Effect("Take Gas", "takegas", new[]{"gas"}, "resources"),
                    new Effect("Raise Supply Limit", "raisesupply", new[]{"supply"}, "resources"),
                    new Effect("Lower Supply Limit", "lowersupply", new[]{"supply"}, "resources"),
                };
                return result;
            }
        }

        public override List<ItemType> ItemTypes => new List<ItemType>(new[]
        {
            new ItemType("Percent", "percent", ItemType.Subtype.Slider, "{\"min\":1,\"max\":100}"),
            new ItemType("Minerals", "minerals", ItemType.Subtype.Slider, "{\"min\":1,\"max\":9999}"),
            new ItemType("Gas", "gas", ItemType.Subtype.Slider, "{\"min\":1,\"max\":9999}"),
            new ItemType("Supply", "supply", ItemType.Subtype.Slider, "{\"min\":1,\"max\":50}"),
            new ItemType("Upgrades", "upgrades", ItemType.Subtype.Slider, "{\"min\":0,\"max\":3}"),
        });

        #endregion

        protected override bool IsReady(EffectRequest request)
        {
            return FindXml() && fileStatus == "playing";
        }

        protected override void StartEffect(EffectRequest request)
        {
            //Log.Message(GetMethods(typeof(EffectRequest)));
            if (!IsReady(request))
            {
                Respond(request, EffectStatus.FailTemporary, "Not ready yet");
                //DelayEffect(request);
                return;
            }

            TryEffect(request,
                () => true, // "condition"
                () => // action
                {
                    try
                    {
                        return SendEffect(request, Method.StartEffect);
                    }
                    catch { return false; }
                },
                () => Connector.SendMessage($"{request.DisplayViewer} invoked {request.InventoryItem}."), // followUp
                null, false, request.FinalCode); // TimeSpan retryDelay, bool retryOnFail, string mutex name, TimeSpan? holdMutex = null
        }

        protected override bool StopEffect(EffectRequest request)
        {
            return true;
        }

        protected override void RequestData(DataRequest request) => Respond(request, request.Key, null, false, $"Variable name \"{request.Key}\" not known.");

        const bool debug = false;

        const string version = "0.23";
        
        string xmlPathRequests = "";
        string xmlPathResponses = "";
        string fileStatus = "";
    }
}
