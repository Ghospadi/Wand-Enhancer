using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using WandEnhancer.Models;

namespace WandEnhancer.Core
{
    public static class EnhancerConfig
    {
        private const int RemoteWebPanelDefaultPort = 3223;
        private static readonly string RemoteWebPanelFallbackUrl = $"http://localhost:{RemoteWebPanelDefaultPort}/remote/";

        public class ResolveContext
        {
            public string Placeholder { get; set; }
            public Func<string, string> Handler { get; set; }
        }

        public class PatchEntry
        {
            public Regex Target { get; set; }
            public string Patch { get; set; }
            public string Name { get; set; }
            public bool Applied { get; set; }
            public bool SingleMatch { get; set; } = true;
            public string[] CandidateFileNames { get; set; }
            public string[] SearchHints { get; set; }
            public ResolveContext Resolver { get; set; }
        }

        public static Dictionary<EPatchType, PatchEntry[]> GetInstance()
        {
            return new Dictionary<EPatchType, PatchEntry[]>()
            {
                {
                    EPatchType.ActivatePro,
                    new[]
                    {
                        new PatchEntry
                        {
                            SearchHints = new[] { "getUserAccount()", "/v3/account" },
                            Resolver = new ResolveContext
                            {
                                Handler = (targetFunction) =>
                                {
                                    var fetchMatch = Regex.Match(targetFunction, @"return\s+this\.#(\w+)\.fetch");
                                    return fetchMatch.Success ? fetchMatch.Groups[1].Value : null;
                                },
                                Placeholder = "<service_name>"
                            },
                            Name = "getUserAccount",
                            Target = new Regex(@"getUserAccount\(\)\{.*?return\s+this\.#\w+\.fetch\(\{.*?\}\)\}",
                                RegexOptions.Singleline),
                            Patch =
                                "getUserAccount(){return this.#<service_name>.fetch({endpoint:\"/v3/account\",method:\"GET\",name:\"/v3/account\",collectMetrics:0}).then(response=>{response.subscription={period:\"yearly\",state:\"active\"};return response;})}"
                        },
                        new PatchEntry
                        {
                            SearchHints = new[] { "setAccountWandBrandExperience()", "/v3/account/brand_experience_wand" },
                            Resolver = new ResolveContext
                            {
                                Handler = (targetFunction) =>
                                {
                                    var match = Regex.Match(targetFunction, @"return\s+this\.#(\w+)\.post");
                                    return match.Success ? match.Groups[1].Value : null;
                                },
                                Placeholder = "<service_name>"
                            },
                            Name = "setAccountWandBrandExperience",
                            Target = new Regex(
                                @"setAccountWandBrandExperience\(\)\{.*?return\s+this\.#\w+\.post\(""/v3/account/brand_experience_wand""\)\}",
                                RegexOptions.Singleline),
                            Patch =
                                "setAccountWandBrandExperience(){return this.#<service_name>.post(\"/v3/account/brand_experience_wand\").then(response=>{response.subscription={period:\"yearly\",state:\"active\"};return response;})}"
                        }
                    }
                },
                {
                    EPatchType.DisableUpdates,
                    new[]
                    {
                        new PatchEntry
                        {
                            CandidateFileNames = new[] { "index.js" },
                            SearchHints = new[] { "ACTION_CHECK_FOR_UPDATE" },
                            Target = new Regex(@"registerHandler\(""ACTION_CHECK_FOR_UPDATE"".*?\)\)\)\)",
                                RegexOptions.Singleline),
                            Patch = "registerHandler(\"ACTION_CHECK_FOR_UPDATE\",(e=>expectUpdateFeedUrl(e,(e=>null)))"
                        }
                    }
                },
                {
                    EPatchType.DevToolsOnF12,
                    new[]
                    {
                        new PatchEntry
                        {
                            Name = "devToolsBeforeInputEvent",
                            CandidateFileNames = new[] { "index.js" },
                            SearchHints = new[] { "whenReady().then(" },
                            // Anchor on the Electron main-process `<app>.whenReady().then(`
                            // call. This site is far more stable than the minified renderer
                            // keydown listener that previously held the F12 -> ACTION_OPEN_DEV_TOOLS
                            // dispatch (its identifiers and shape change on every Wand release).
                            // We attach a `before-input-event` hook to every BrowserWindow's
                            // webContents which toggles DevTools on F12 directly from the main
                            // process, bypassing the renderer dispatcher entirely.
                            Target = new Regex(@"(?<app>\w+)\.whenReady\(\)\.then\("),
                            Patch = "${app}.on(\"browser-window-created\",((_,w)=>{try{w.webContents.on(\"before-input-event\",((_,i)=>{if(\"F12\"===i.key&&\"keyDown\"===i.type){w.webContents.isDevToolsOpened()?w.webContents.closeDevTools():w.webContents.openDevTools({mode:\"detach\"})}}))}catch(e){}})),${app}.whenReady().then("
                        }
                    }
                },
                {
                    EPatchType.RemoteWebPanelPreview,
                    new[]
                    {
                        new PatchEntry
                        {
                            Name = "remoteBridgeMainBoot",
                            CandidateFileNames = new[] { "index.js" },
                            SearchHints = new[] { "whenReady().then(run)" },
                            Target = new Regex(@"(?<app>\w+)\.whenReady\(\)\.then\(run\)"),
                            Patch = "${app}.whenReady().then(()=>{try{const p=require(\"node:path\");require(p.join(__dirname,\"remote-panel\",\"bridge.cjs\")).installWandRuntime(require(\"electron\"));}catch(e){try{const fs=require(\"node:fs\"),os=require(\"node:os\"),p=require(\"node:path\");fs.appendFileSync(p.join(os.tmpdir(),\"wand-remote-bridge.log\"),\"[\"+new Date().toISOString()+\"] [boot-error] \"+(e&&e.stack||e)+\"\\n\");}catch(_){}}return run()})"
                        }
                    }
                }
            };
        }
    }
}
