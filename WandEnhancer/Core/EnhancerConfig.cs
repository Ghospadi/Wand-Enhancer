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
                        },
                        new PatchEntry
                        {
                            Name = "remoteBridgeReset",
                            SearchHints = new[] { "client-state" },
                            Target = new Regex(
                                @"#et\(\)\{this\.#Re&&\(this\.#Re\.dispose\(\),this\.#Re=null\),this\.#Ee=Date\.now\(\)\.toString\(\),this\.#Ne=null,this\.#je=\[],this\.#\$e=null\}"),
                            Patch = "#et(){this.#Re&&(this.#Re.dispose(),this.#Re=null),this.#Ee=Date.now().toString(),this.#Ne=null,this.#je=[],this.#$e=null,this.__wandRemoteTrainerInfo=null,this.__wandRemoteBridge?.sync(null)}"
                        },
                        new PatchEntry
                        {
                            Name = "remoteBridgeSyncSnapshot",
                            SearchHints = new[] { "client-state" },
                            Target = new Regex(
                                @"#Xe\(\)\{if\(this\.status===i\.Connected\)\{let e,t=!1,s=this\.#\$e\?\.getMetadata\(l\.vO\)\?\.gameVersion\?\?null,i=!1;const a=this\.#ze\[this\.#Ne\?\?""""\]\|\|null;this\.#V&&\(e=this\.#Ve\.getPreferredInstallationInfo\(this\.#V\),e\.app&&\(t=!0,s\?\?=e\.version\?\?null,i=""number""==typeof e\.version&&!this\.#je\.includes\(e\.version\)\)\),this\.#xe\?\.send\(""client-state"",\{instanceId:this\.#Ee,trainerId:this\.#Ne,trainerLoading:this\.#\$e\?\.isLoading\(\),gameInstalled:t,gameVersion:s,needsCompatibilityWarning:i,values:this\.#tt\(\),themeId:this\.#Je,settings:P\(this\.settings\),language:this\.#We,accountUuid:this\.account\.uuid,notesReadHash:a\}\)\}\}"),
                            Patch = "#Xe(){if(this.status===i.Connected){let e,t=!1,s=this.#$e?.getMetadata(l.vO)?.gameVersion??null,i=!1;const a=this.#ze[this.#Ne??\"\"]||null;this.#V&&(e=this.#Ve.getPreferredInstallationInfo(this.#V),e.app&&(t=!0,s??=e.version??null,i=\"number\"==typeof e.version&&!this.#je.includes(e.version))),this.#xe?.send(\"client-state\",{instanceId:this.#Ee,trainerId:this.#Ne,trainerLoading:this.#$e?.isLoading(),gameInstalled:t,gameVersion:s,needsCompatibilityWarning:i,values:this.#tt(),themeId:this.#Je,settings:P(this.settings),language:this.#We,accountUuid:this.account.uuid,notesReadHash:a}),this.__wandRemoteBridge?.sync({instanceId:this.#Ee,trainerId:this.#Ne,trainerInfo:this.__wandRemoteTrainerInfo??null,metadata:this.#$e?.getMetadata(l.vO)??null,trainerLoading:this.#$e?.isLoading()??false,gameInstalled:t,gameVersion:s,needsCompatibilityWarning:i,language:this.#We,themeId:this.#Je,notesReadHash:a,values:this.#tt()})}}"
                        },
                        new PatchEntry
                        {
                            Name = "remoteBridgeBindHandler",
                            SearchHints = new[] { "setCurrentTrainer", "client-state" },
                            Target = new Regex(
                                @"setCurrentTrainer\(e,t=null\)\{const s=e\?\.trainerId\|\|null,i=\(s\?e\?\.gameId:null\)\|\|null,a=\(s\?e\?\.supportedVersions:null\)\|\|\[];if\(s===this\.#Ne&&t===this\.#\$e\)return;"),
                            Patch = "setCurrentTrainer(e,t=null){this.__wandRemoteBridge||(this.__wandRemoteBridge=(()=>{try{const r=globalThis.require||require;const{ipcRenderer:c}=r(\"electron\");try{c.invoke(\"wand-remote-url\").then((u=>{u&&(globalThis.__wandRemoteBridgeUrl=u)}))}catch(e){}const send=(ch,p)=>{try{return c.invoke(ch,p&&JSON.parse(JSON.stringify(p)))}catch(e){}};return{sync:(s)=>send(\"wand-remote-sync\",s),valueChanged:(s)=>send(\"wand-remote-value-changed\",s),setHandler:(h)=>{if(this.__wandRemoteBridgeBound)return;this.__wandRemoteBridgeBound=true;try{c.invoke(\"wand-remote-set-handler-bind\")}catch(e){}c.on(\"wand-remote-set-value\",(_e,req)=>{try{h(req)}catch(e){}})}}}catch(e){try{const r=globalThis.require||require,fs=r(\"node:fs\"),os=r(\"node:os\"),p=r(\"node:path\");fs.appendFileSync(p.join(os.tmpdir(),\"wand-remote-bridge.log\"),\"[\"+new Date().toISOString()+\"] [renderer-bind-error] \"+(e&&e.stack||e)+\"\\n\");}catch(_){}return null}})());this.__wandRemoteBridge?.setHandler((e=>{if(!this.#$e||!e?.target)return!1;return this.#$e.isActive()?this.#$e.setValue(e.target,e.value,3,e.cheatId):!1}));this.__wandRemoteTrainerInfo=e??null;const s=e?.trainerId||null,i=(s?e?.gameId:null)||null,a=(s?e?.supportedVersions:null)||[];if(s===this.#Ne&&t===this.#$e)return;"
                        },
                        new PatchEntry
                        {
                            Name = "remoteBridgeValueDelta",
                            SearchHints = new[] { "client-value-changed" },
                            Target = new Regex(
                                @"#yt\(e,t\)\{t\.push\(e\.onValueSet\(\(e=>\{this\.status===i\.Connected&&3!==e\.source&&this\.#xe\?\.send\(""client-value-changed"",\{instanceId:this\.#Ee,name:e\.name,value:e\.value,cheatId:e\.cheatId\}\)\}\)\)\),this\.#Xe\(\)\}"),
                            Patch = "#yt(e,t){t.push(e.onValueSet((e=>{this.status===i.Connected&&3!==e.source&&this.#xe?.send(\"client-value-changed\",{instanceId:this.#Ee,name:e.name,value:e.value,cheatId:e.cheatId}),this.__wandRemoteBridge?.valueChanged({trainerId:this.#Ne,target:e.name,value:e.value,oldValue:e.oldValue,source:String(e.source??\"desktop\"),cheatId:e.cheatId})}))),this.#Xe()}"
                        }
                    }
                }
            };
        }
    }
}
