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

namespace CrowdControl.Games.Packs
{
    public enum Method {
        StartEffect,
        StopEffect,
    }

    public class SC2EffectPack : PCEffectPack<NullConnector>
    {
        private const bool debug = false;

        private const string ccEffectPackVersion = "0.2.0";
        private const string xmlSchemaVersion = "0.1.0";

        // gonna need to crawl around for the correct file
            // for test games C:\Users\die4e\Documents\StarCraft II\Banks\
            // for real games C:\Users\die4e\Documents\StarCraft II\Accounts\12345\5-S9-2-543536\Banks\1-S2-1-258901
            // the first number (12345) is the player's account id
            // the 2nd number is maybe for the region?
            // the last number is the map author's ID? (not the mod author's)
            // we can use the status and date in the file to determine which one to use
            // we could also make the mod delete the bank instead of setting the status to exited?
            // search in the Accounts folder first, then the test folder if we don't find it
        
        private static string connectorPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "StarCraft II/Banks");
        private static string xmlPathRequests = Path.Join(connectorPath, "CrowdControl.SC2Bank");
        private static string xmlPathResponses = Path.Join(connectorPath, "CrowdControlResponses.SC2Bank");

        // First argument is an effect pack index assigned internally by Warp World for official effect packs.
        // We can use any integer here since it's ignored for any SDK/ccpak plugin.
        public override Game Game { get; } = new Game(99999, "StarCraft 2 Randomizer", "SC2EffectPack", "PC", ConnectorType.NullConnector);

        public SC2EffectPack([NotNull] IPlayer player, [NotNull] Func<CrowdControlBlock, bool> responseHandler, [NotNull] Action<object> statusUpdateHandler)
            : base(player, responseHandler, statusUpdateHandler)
        {
            Directory.CreateDirectory(connectorPath);
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
            <Value string=""0.23""/>
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

        protected bool XmlCheck(EffectRequest request, Method method) {
            // TODO
            return true;

            if (!File.Exists(xmlPathResponses)) {
                return false;
            }

            string outXml = File.ReadAllText(xmlPathResponses);
            string eventIndexXml = Regex.Match(outXml, @"<eventIndex>\s*(.*)\s*<\/eventIndex>", RegexOptions.Singleline).Groups[1].Value;
            string eventsXml = Regex.Match(eventIndexXml, @"<events>\s*(.*)\s*<\/events>", RegexOptions.Singleline).Groups[1].Value;
            MatchCollection eventXmls = Regex.Matches(eventsXml, @"<event>\s*?(.*?)\s*?<\/event>", RegexOptions.Singleline);

            foreach (Match eventXmlMatch in eventXmls) {
                string eventXml = eventXmlMatch.Groups[1].Value;

                if (
                    Regex.IsMatch(eventXml, $@"<ID>\s*?{Regex.Escape(request.ID.ToString())}\s*?<\/ID>", RegexOptions.Singleline) &&
                    Regex.IsMatch(eventXml, $@"<executed>\s*?[Tt]rue\s*?<\/executed>", RegexOptions.Singleline)
                ) {
                    return true;
                }
            }
            return false;
        }

        protected bool XmlWait(EffectRequest request, Method method, int millisecondsTimeout = 5000, int millisecondsCheckInterval = 500) {
            return SpinWait.SpinUntil(() => {
                Thread.Sleep(millisecondsCheckInterval);
                return XmlCheck(request, method);
            }, millisecondsTimeout);
        }

        protected bool SendEffect(EffectRequest request, Method method) {
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
                    new Effect("Debug Message", "debug"),

                    new Effect("Mean Things", "mean", ItemKind.Folder),
                    new Effect("Nuke All Town Halls", "nukes", "mean"),
                    new Effect("Kill All Workers", "killworkers", "mean"),
                    new Effect("Kill All Army", "killarmy", "mean"),

                    new Effect("Nice Things", "nice", ItemKind.Folder),
                    new Effect("Give Minerals", "giveminerals", new[]{"minerals"}, "nice"),
                    new Effect("Give Gas", "givegas", new[]{"gas"}, "nice"),
                };
                return result;
            }
        }

        public override List<ItemType> ItemTypes => new List<ItemType>(new[]
        {
            new ItemType("Percent", "percent", ItemType.Subtype.Slider, "{\"min\":1,\"max\":100}"),
            new ItemType("Minerals", "minerals", ItemType.Subtype.Slider, "{\"min\":1,\"max\":10000}"),
            new ItemType("Gas", "gas", ItemType.Subtype.Slider, "{\"min\":1,\"max\":10000}")
        });

        #endregion

        protected override bool IsReady(EffectRequest request)
        {
            //TODO: Implement
            return true;
        }

        protected override void StartEffect(EffectRequest request)
        {
            if (!IsReady(request))
            {
                DelayEffect(request);
                return;
            }

            TryEffect(request,
                () => true,
                () =>
                {
                    try
                    {
                        return SendEffect(request, Method.StartEffect);
                    }
                    catch { return false; }
                },
                () => Connector.SendMessage($"{request.DisplayViewer} invoked {request.InventoryItem}."),
                null, true, request.FinalCode);
        }

        protected override bool StopEffect(EffectRequest request)
        {
            return true;
        }

        protected override void RequestData(DataRequest request) => Respond(request, request.Key, null, false, $"Variable name \"{request.Key}\" not known.");
    }
}
