import{i as e}from"./preload-helper-xPQekRTU.js";import{d as t,f as n,o as r,p as i,u as a}from"./App-pZMLD6xW.js";import{f as ee,p as te}from"./RuntimeFirstRunOnboarding-Qfzu4AGE.js";import{i as ne,n as re,r as o,t as ie}from"./sideportFixtures-DqaTFpa_.js";var s,c,l,u,d,ae,f,oe,p,se,m,h,g,ce,le,_,v,y,ue,de,fe,pe,me,b,x,he,ge,_e,ve,ye,be,xe,S,C,Se,Ce,we,Te,Ee,De,Oe,ke,Ae,je,Me,Ne,Pe,Fe,Ie,Le,Re,ze,Be,w,Ve,He,Ue,We,Ge,T,E,D,O,k,A,j,M,N,P,F,I,L,R,Ke,z,B,V,H,U,W,G,K,q,J,Y,X,qe,Je,Ye,Xe,Z,Ze,Qe,$e,et,tt,nt,rt,it,at,ot,Q,$,st;e((()=>{t(),a(),ne(),ee(),{expect:s,fn:c,userEvent:l,waitFor:u,within:d}=__STORYBOOK_MODULE_TEST__,ae={title:`Sideport/Admin Shell`,component:r,args:{initialSetupOpen:!0},parameters:{docs:{description:{component:`Storybook renders the GitHub Pages demo portal with fixture data. The production bundle uses the live .NET API and keeps demo fixtures out of runtime imports.`}}}},f={mode:`demo`,baseUrl:`storybook://demo-data`,message:`Demo data for GitHub Pages and design review.`,canMutate:!1},oe={mode:`unavailable`,baseUrl:`/sideport-api`,message:`No Sideport API is reachable. Runtime pages stay empty until the .NET backend responds.`,canMutate:!1},p={mode:`partial`,baseUrl:`/`,message:`Protected API calls are returning 401. Save the browser session token in Settings.`,canMutate:!0},se={...o,workspace:{...o.workspace,currentMember:{id:`u-owner`,name:`Dragos`,email:`dragos@example.test`,role:`owner`,status:`active`,source:`demo`}}},m=c(e=>e),h=e=>{let t=[`server`,`apple-signer`,`device`,`app`,`install`,`ready`],n=t.indexOf(e);return t.map((e,t)=>({id:e,state:t<=n?`complete`:t===n+1?`action-required`:`not-started`,required:!0,source:`demo`,reason:t<=n?`${e} evidence is complete.`:`${e} still needs attention.`,evidence:[]}))},g={preflightId:`install_preflight_story`,expiresAt:`2026-07-12T13:00:00Z`,planVersion:`sha256:storybook-plan`,ready:!0,blockers:[],warnings:[],plannedMutations:[`Create the pending registration`,`Sign and install over USB`,`Verify the app on the iPhone`],scarceLimits:[{code:`app-slots`,label:`Sideport app slots`,used:1,limit:3}],requiresConfirmation:!0,source:`demo`},ce={schemaVersion:2,completedAt:`2026-07-12T12:00:00Z`,actor:{kind:`oidc-user`,displayName:`owner@example.test`},accountProfileId:`demo-personal-account`,teamId:`DEMO123456`,deviceUdid:`00008030-FAKE-BB8F23A0C02E`,registrationKey:{deviceUdid:`00008030-FAKE-BB8F23A0C02E`,bundleId:`com.example.certcountdown`},verifiedOperationId:`op-onboarding-install`,schedulerSettingsVersion:`settings-story`,operationalCheckedAt:`2026-07-12T11:59:59Z`},le={...f,baseUrl:`storybook://fresh-onboarding`,onboarding:{firstRunComplete:!1,schedulerEnabled:!1,steps:[],setupState:`in-progress`,completionReceipt:null,workflow:{schemaVersion:2,setupState:`in-progress`,readyNow:!1,completedAt:null,verifiedOperationId:null,nextAction:{stepId:`apple-signer`,action:`connect`,label:`Connect Apple`},steps:h(`server`)}}},_={...f,baseUrl:`storybook://interactive-onboarding`,canMutate:!0,onboarding:{firstRunComplete:!1,schedulerEnabled:!1,steps:[],setupState:`in-progress`,completionReceipt:null,workflow:{schemaVersion:2,setupState:`in-progress`,readyNow:!1,completedAt:null,verifiedOperationId:null,nextAction:{stepId:`app`,action:`choose-app`,label:`Choose app`},steps:h(`device`)}}},v={...f,baseUrl:`storybook://device-onboarding`,canMutate:!0,onboarding:{firstRunComplete:!1,schedulerEnabled:!1,steps:[],setupState:`in-progress`,completionReceipt:null,workflow:{schemaVersion:2,setupState:`in-progress`,readyNow:!1,completedAt:null,verifiedOperationId:null,nextAction:{stepId:`device`,action:`start-enrollment`,label:`Add iPhone`},steps:h(`apple-signer`)}}},y={...f,baseUrl:`storybook://completed-onboarding`,canMutate:!0,onboarding:{firstRunComplete:!0,schedulerEnabled:!0,steps:[],setupState:`complete`,completionReceipt:ce,workflow:{schemaVersion:2,setupState:`complete`,readyNow:!0,completedAt:ce.completedAt,verifiedOperationId:ce.verifiedOperationId,nextAction:null,steps:h(`ready`)}}},ue={...o,workspace:{...o.workspace,currentMember:o.workspace.members.find(e=>e.role===`family`),capabilities:{"devices.enroll":!0,"operations.run":!0}}},de=`op-onboarding-finalization-recovery`,fe={..._,mode:`live`,baseUrl:`storybook://onboarding-finalization-recovery`,onboarding:{firstRunComplete:!1,schedulerEnabled:!1,steps:[],setupState:`in-progress`,selectedCatalogAppId:o.catalogApps[0].id,completionReceipt:null,workflow:{schemaVersion:2,setupState:`in-progress`,readyNow:!1,completedAt:null,verifiedOperationId:null,nextAction:{stepId:`install`,action:`retry-finalization`,label:`Retry finishing setup`},steps:h(`app`).map(e=>e.id===`install`?{...e,source:`live`,state:`action-required`,activeOperationId:de,nextAction:{action:`retry-finalization`,label:`Retry finishing setup`}}:{...e,source:`live`})}}},pe={operationId:de,type:`install`,status:`waiting`,retryable:!0,target:{kind:`catalog-app`,deviceUdid:o.devices[1].udid,bundleId:`com.example.certcountdown`},result:{success:!0,bundleId:`com.example.certcountdown`,expiresAt:`2026-07-19T12:00:00Z`},error:{code:`onboarding-operational-check-failed`,message:`Sideport is waiting to save automatic refresh.`},stages:[{id:`verify`,label:`Verify on iPhone`,status:`succeeded`,message:`The app was verified.`},{id:`activate-registration`,label:`Activate app`,status:`succeeded`,message:`The registration is active.`},{id:`enable-scheduler`,label:`Enable automatic refresh`,status:`running`,message:`Waiting for operational checks.`},{id:`write-completion-receipt`,label:`Finish setup`,status:`pending`,message:`Waiting for automatic refresh.`}]},me=c(async e=>ce),b=`op-onboarding-install-unknown`,x={..._,baseUrl:`storybook://onboarding-reconcile`,onboarding:{firstRunComplete:!1,schedulerEnabled:!1,steps:[],setupState:`in-progress`,selectedCatalogAppId:o.catalogApps[0].id,completionReceipt:null,workflow:{schemaVersion:2,setupState:`in-progress`,readyNow:!1,nextAction:{stepId:`install`,action:`reconcile-install`,label:`Check iPhone status`},steps:h(`app`).map(e=>e.id===`install`?{...e,state:`blocked`,activeOperationId:b,nextAction:{action:`reconcile-install`,label:`Check iPhone status`},reason:`The USB transfer ended without a confirmed result.`}:e)}}},he={operationId:b,type:`install`,status:`unknown`,target:{kind:`app`,deviceUdid:o.devices[1].udid,bundleId:`com.example.certcountdown`},error:{code:`install-outcome-unknown`,message:`The USB transfer ended without a confirmed result.`},stages:[{id:`install`,label:`Install app`,status:`unknown`,message:`The result is unknown.`}]},ge=c(async e=>({operationId:`op-onboarding-reconcile-child`,parentOperationId:e,type:`reconcile`,status:`queued`,target:he.target,stages:[{id:`verify`,label:`Check iPhone`,status:`pending`,message:`Waiting to read the iPhone.`}]})),_e={operationId:`op-onboarding-reconcile-child`,parentOperationId:b,type:`reconcile`,status:`blocked`,target:he.target,error:{code:`reconciliation-evidence-mismatch`,message:`The installed version does not match the unknown install.`},stages:[{id:`verify`,label:`Check iPhone`,status:`blocked`,message:`The installed version does not match.`,error:{message:`The installed version does not match the unknown install.`}}]},ve=e=>({operationId:`op-${e.finishOnboarding?`home`:`standalone`}-install`,type:`install`,status:`queued`,target:{kind:`app`,deviceUdid:e.deviceUdid,bundleId:e.bundleId},stages:[{id:`preflight`,label:`Check install`,status:`pending`,message:`Waiting to start.`}]}),ye=async e=>({bundleId:o.catalogApps.find(t=>t.id===e.catalogAppId)?.expectedBundleId,deviceUdid:e.deviceUdid,lifecycle:e.lifecycle,catalogAppId:e.catalogAppId}),be=c(ye),xe=c(async e=>g),S={...te,system:{...o.system,scheduler:{enabled:!1,source:`demo`}},catalogApps:o.catalogApps,personalApple:{...te.personalApple,state:`not-configured`,secretCustody:`sideport-managed-encrypted-store`,credentialEntry:{supported:!0,allowedNow:!0,blockedReason:null},message:`No Apple account is connected yet.`,source:`demo`},workspace:o.workspace},C={...S,devices:[o.devices[1]],personalApple:{...S.personalApple,state:`authenticated`,accountProfileId:`demo-personal-account`,appleIdHint:`a***@example.test`,selectedTeamId:`DEMO123456`,message:`Apple accepted the account and returned one Personal Team.`,teams:[{teamId:`DEMO123456`,name:`Example Personal Team`,type:`Personal Team`}]}},Se={...C,devices:[],system:{...C.system,operational:!1,checks:[...C.system.checks.filter(e=>e.id!==`device-transport`),{id:`device-transport`,status:`fail`,source:`demo`,checkedAt:`2026-07-14T01:20:00Z`,scope:`iphone`,affectedResources:[`usbmux-transport`],reason:`Sideport cannot reach the iPhone transport.`,nextAction:`Connect the iPhone over USB.`}]}},Ce={...C,system:{...C.system,scheduler:{enabled:!0,checkedAt:`2026-07-12T12:00:00Z`,policy:{mode:`due-only`,evaluationInterval:`01:00:00`,refreshLeadTime:`2.00:00:00`,resignInterval:null,catchUp:`evaluate-on-startup`,missedIntervals:`not-replayed`},nextEvaluationAt:`2026-07-12T13:00:00Z`,concurrency:{maxRunning:1,lockState:`idle`,operationId:null},source:`demo`}},installedApps:[{bundleId:`com.example.certcountdown`,deviceUdid:o.devices[1].udid,name:`Cert Clock`,version:`0.1.0`,managedBySideport:!0,source:`demo`}],apps:[{...o.apps[0],deviceUdid:o.devices[1].udid,teamId:`DEMO123456`,lastSucceeded:!0,lastError:null,displayName:{value:`Cert Clock`,source:`demo`},version:{value:`0.1.0`,source:`demo`}}]},we=`op-onboarding-terminal-lineage`,Te={...x,baseUrl:`storybook://onboarding-terminal-lineage`,onboarding:{...x.onboarding,activeInstallOperationId:we,workflow:{...x.onboarding.workflow,nextAction:{stepId:`install`,action:`review-install`,label:`Review install`},steps:x.onboarding.workflow.steps.map(e=>e.id===`install`?{...e,activeOperationId:we,nextAction:{action:`review-install`,label:`Review install`},reason:`The saved IPA changed after device verification.`}:e)}}},Ee={operationId:we,type:`install`,status:`blocked`,retryable:!1,target:he.target,result:{success:!0,bundleId:`com.example.certcountdown`,version:`0.1.0`,expiresAt:`2026-07-19T12:00:00Z`},error:{code:`onboarding-artifact-lineage-unavailable`,message:`The saved IPA changed after device verification.`},stages:[{id:`verify`,label:`Verify on iPhone`,status:`succeeded`,message:`The app was verified.`},{id:`write-completion-receipt`,label:`Finish setup`,status:`blocked`,message:`The saved IPA changed.`,error:{message:`The saved IPA changed after device verification.`}}]},De=`op-standalone-resume`,Oe={...C,operations:[{operationId:De,type:`install`,status:`running`,createdAt:`2026-07-12T12:10:00Z`,updatedAt:`2026-07-12T12:10:05Z`,deviceUdid:o.devices[1].udid,bundleId:`com.example.certcountdown`,actor:`owner@example.test`,stages:[{id:`install`,label:`Install app`,status:`running`,message:`Installing over USB.`}],cancelable:!1,retryable:!1,rerunnable:!1,finishOnboarding:!1,source:`demo`}]},ke={...o,apps:[{...o.apps[0],lifecycle:`pending-install`,lastSucceeded:null,lastError:null,expiresAt:void 0,lastVerifiedOperationId:null}]},Ae=c(async e=>({enabled:e,checkedAt:`2026-07-12T12:05:00Z`,policy:Ce.system.scheduler.policy,nextEvaluationAt:e?`2026-07-12T13:05:00Z`:null,lastEvaluation:null,dueCount:0,queuedCount:0,concurrency:{maxRunning:1,lockState:`idle`,operationId:null},historyRetention:{maxEvaluations:100},source:`live`})),je={loadImportRoots:async()=>[{id:`apps`,label:`Sideport app storage`,available:!0}],upload:async e=>({id:`uploaded-app`,name:e.name.replace(/\.ipa$/i,``),purpose:`Uploaded and inspected.`,versionLabel:`1.0`,status:`ready`}),importFromRoot:async()=>({id:`stored-app`,name:`Stored app`,purpose:`Imported from configured storage.`,versionLabel:`1.0`,status:`ready`}),loadGitHubSources:async()=>({capability:{kind:`github-app`,supported:!0,allowedNow:!0},sources:[{id:`public-sideport`,repository:`dragoshont/sideport`,visibility:`public`,status:`connected`}]}),connectGitHub:async(e,t)=>({id:`connection-1`,repository:e,visibility:t,status:`connected`,sourceId:`connected-source`}),loadGitHubReleases:async e=>({sourceId:e,repository:`dragoshont/sideport`,releases:[{releaseId:17,tag:`sample-apps`,name:`Sample apps`,prerelease:!1,assets:[{assetId:41,name:`Cert-Clock.ipa`,sizeBytes:18432,digest:`sha256:demo`,importable:!0}]}]}),importGitHub:async()=>({id:`cert-clock-github`,name:`Cert Clock`,purpose:`Imported from a GitHub release.`,versionLabel:`0.1.0`,status:`ready`})},Me={...je,loadImportRoots:async()=>{throw Error(`Configured storage is offline.`)}},Ne={start:async()=>({operationId:`op-enroll-story`,status:`waiting`,stages:[{id:`wait-for-usb`,status:`waiting`,message:`Waiting for iPhone.`}]}),read:async()=>({operationId:`op-enroll-story`,status:`succeeded`,stages:[{id:`accept-device`,status:`succeeded`,message:`iPhone added.`}],result:{deviceEnrollment:{selectedDeviceUdid:`000081-story`,inventoryState:`accepted`}}})},Pe=c(async()=>({operationId:`op-onboarding-enroll-story`,status:`waiting`,stages:[{id:`wait-for-usb`,status:`waiting`,message:`Waiting for iPhone.`}]})),Fe={start:Pe,read:async()=>({operationId:`op-onboarding-enroll-story`,status:`waiting`,stages:[{id:`wait-for-usb`,status:`waiting`,message:`Waiting for iPhone.`}]})},Ie=c(async()=>({operationId:`unexpected-new-operation`,status:`waiting`,stages:[]})),Le=c(async()=>({operationId:`op-onboarding-enroll-resume`,status:`waiting`,stages:[{id:`wait-for-usb`,status:`waiting`,message:`Still waiting for iPhone.`}]})),Re={start:Ie,read:Le},ze={...v,mode:`live`,onboarding:{...v.onboarding,workflow:{...v.onboarding.workflow,nextAction:null,steps:v.onboarding.workflow.steps.map(e=>e.id===`device`?{...e,state:`in-progress`,activeOperationId:`op-onboarding-enroll-resume`,reason:`Waiting for iPhone over USB.`,nextAction:void 0}:e)}}},Be={operationId:`op-enroll-recovery-source`,status:`recovery-required`,retryable:!0,stages:[{id:`request-pairing`,status:`succeeded`,message:`Trust was already requested.`}],error:{code:`device-enrollment-recovery-required`,message:`Reconnect this iPhone so Sideport can check the existing Trust request.`}},w=c(async()=>Be),Ve=c(async e=>({operationId:`op-enroll-recovery-child`,status:`waiting`,retryable:!1,stages:[{id:`request-pairing`,status:`succeeded`,message:`The earlier Trust request will not be repeated.`},{id:`verify-lockdown`,status:`waiting`,message:`Checking Trust.`}]})),He=c(async()=>({operationId:`op-enroll-recovery-child`,status:`succeeded`,stages:[{id:`accept-device`,status:`succeeded`,message:`iPhone added.`}],result:{deviceEnrollment:{selectedDeviceUdid:`000081-recovery`,inventoryState:`accepted`}}})),Ue={start:w,retry:Ve,read:He},We=c(async()=>{throw Error(`Sideport briefly lost the secure iPhone connection.`)}),Ge={start:w,retry:We,read:async()=>Be},T=(e,t)=>({name:t,args:{data:o,apiStatus:f,initialRoute:e}}),E=T(`home`,`Overview - healthy mixed fleet`),D={name:`First Run Onboarding`,args:{data:S,apiStatus:le,initialRoute:`home`},parameters:{docs:{description:{story:`The production six-step runtime shell with a deterministic fresh-deployment read model: no Apple signer, accepted iPhone, installation, or scheduler.`}}},play:async({canvasElement:e})=>{let t=d(e);await s(t.getByTestId(`runtime-first-run-onboarding`)).toBeVisible(),await s(t.getByRole(`heading`,{name:`Connect Apple`})).toBeVisible(),await s(t.getAllByRole(`main`)).toHaveLength(1),await s(t.getByText(`1 of 6 complete`)).toBeVisible()}},O={name:`Owner account - portal access before iPhone setup`,args:{data:S,apiStatus:le,initialRoute:`home`,initialSetupOpen:!1},play:async({canvasElement:e})=>{let t=d(e);await s(t.getByRole(`heading`,{name:`Apps and iPhones at a glance`})).toBeVisible(),await s(t.getByText(`Your Owner account is ready`)).toBeVisible(),await s(t.getByText(/Next: Connect Apple/)).toBeVisible(),await s(t.queryByTestId(`runtime-first-run-onboarding`)).not.toBeInTheDocument(),await l.click(t.getByRole(`button`,{name:`Continue setup`})),await s(t.getByTestId(`runtime-first-run-onboarding`)).toBeVisible(),await s(t.getByRole(`heading`,{name:`Connect Apple`})).toBeVisible(),await l.click(t.getByRole(`button`,{name:`Set up later`})),await s(t.getByRole(`heading`,{name:`Apps and iPhones at a glance`})).toBeVisible(),await s(t.getByRole(`button`,{name:`Continue setup`})).toBeVisible()}},k={name:`First Run - missing iPhone is actionable and automatic`,args:{data:Se,apiStatus:{...v,mode:`live`},initialRoute:`home`,addIPhoneServices:Fe,iPhoneSoundPlayer:m,iPhoneAttentionDelayMs:20},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body);Pe.mockClear(),m.mockClear();let r=d(t.getByTestId(`runtime-onboarding-panel-device`));await s(r.getByRole(`heading`,{name:`Connect iPhone`})).toBeVisible(),await s(r.getByText(`Connect the iPhone now`)).toBeVisible(),await s(r.getByText(`Use a data-capable cable and plug the iPhone directly into the computer running Sideport.`)).toBeVisible(),await s(r.getByText(`When asked, tap Trust This Computer and enter the iPhone passcode.`)).toBeVisible(),await s(r.getByText(/advance automatically/)).toBeVisible(),await s(r.getByRole(`button`,{name:`Start connecting`})).toBeEnabled(),await s(r.queryByRole(`button`,{name:/Pair|I tapped Trust|Add to Sideport/})).not.toBeInTheDocument(),await s(t.getByText(`2 of 6 complete`)).toBeVisible(),await l.click(r.getByRole(`button`,{name:`Start connecting`}));let i=n.getByRole(`dialog`,{name:`Add an iPhone`});await u(()=>s(Pe).toHaveBeenCalledTimes(1)),await s(d(i).getByRole(`button`,{name:`Close Add iPhone`})).toHaveFocus(),await s(d(i).getByRole(`button`,{name:`Continue`})).toBeDisabled(),await s(d(i).getByText(`Waiting for your iPhone…`)).toBeVisible(),await s(m).toHaveBeenCalledWith(`listening`),await u(()=>s(d(i).getByText(/Still waiting/)).toBeVisible()),await s(m).toHaveBeenCalledWith(`attention`),await s(d(i).queryByRole(`button`,{name:`Connect iPhone`})).not.toBeInTheDocument(),await new Promise(e=>window.setTimeout(e,1200)),await s(Pe).toHaveBeenCalledTimes(1)}},A={name:`First Run - active iPhone connection resumes without another start`,args:{data:Se,apiStatus:ze,initialRoute:`home`,addIPhoneServices:Re},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body);Ie.mockClear(),Le.mockClear(),await l.click(d(t.getByTestId(`runtime-onboarding-panel-device`)).getByRole(`button`,{name:`Show connection status`}));let r=n.getByRole(`dialog`,{name:`Add an iPhone`});await u(()=>s(Le).toHaveBeenCalled()),await s(Ie).not.toHaveBeenCalled(),await s(d(r).getByRole(`button`,{name:`Continue`})).toBeDisabled()}},j={name:`First Run - selection survives a runtime remount`,args:{data:C,apiStatus:_,initialRoute:`home`},play:async({canvasElement:e})=>{let t=d(e);await s(t.getByTestId(`runtime-first-run-onboarding`)).toBeVisible();let n=t.getByRole(`navigation`,{name:`First-run setup steps`});await s(n).toBeVisible(),await s(d(n).getByRole(`button`,{name:/Check Sideport/})).toBeVisible(),await s(d(n).getByRole(`button`,{name:/Connect Apple/})).toBeVisible(),await s(d(n).getByRole(`button`,{name:/Connect iPhone/})).toBeVisible(),await s(d(n).getByRole(`button`,{name:/Choose app/})).toBeVisible(),await s(d(n).getByRole(`button`,{name:/Install/})).toBeVisible(),await s(d(n).getByRole(`button`,{name:/Ready/})).toBeVisible();let r=d(t.getByTestId(`runtime-onboarding-panel-app`)).getAllByRole(`radio`);await l.click(r[0]),await l.keyboard(`{ArrowDown}`),await s(r[1]).toBeChecked(),await s(t.getByTestId(`runtime-onboarding-live-region`)).toHaveTextContent(`Dice Roll selected`),await l.click(t.getByRole(`button`,{name:`Show technical details`})),await s(t.getByText(`Browser-session app choice · non-authoritative`)).toBeVisible()}},M={name:`First Run - install starts inline once`,args:{data:C,apiStatus:{..._,baseUrl:`storybook://onboarding-install-start`},initialRoute:`home`,registerPendingAppService:be,preflightInstallService:xe,installAppService:async e=>{if(!e.finishOnboarding)throw Error(`The onboarding install must carry finishOnboarding=true.`);if(e.preflightId!==g.preflightId||e.planVersion!==g.planVersion)throw Error(`The confirmed preflight was not submitted.`);if(e.catalogAppId!==o.catalogApps[0].id||e.accountProfileId!==`demo-personal-account`)throw Error(`The selected catalog app and Apple account were not bound to the install.`);return ve(e)},readOperationService:async e=>({...ve({deviceUdid:o.devices[1].udid,bundleId:`com.example.certcountdown`,catalogAppId:o.catalogApps[0].id,accountProfileId:`demo-personal-account`,preflightId:`install_preflight_story`,planVersion:`sha256:storybook-plan`,finishOnboarding:!0,confirmedPlannedMutations:!0,idempotencyKey:`story`}),operationId:e,status:`succeeded`,stages:[{id:`verify`,label:`Verify on iPhone`,status:`succeeded`,message:`Verified.`}]})},play:async({canvasElement:e})=>{be.mockClear(),xe.mockClear();let t=d(e),n=d(t.getByTestId(`runtime-onboarding-panel-app`));await l.click(n.getAllByRole(`radio`)[0]),await l.click(n.getByRole(`button`,{name:/Continue to install/}));let r=d(t.getByTestId(`runtime-onboarding-panel-install`));await s(await r.findByRole(`button`,{name:`Install and finish`})).toBeEnabled(),await s(be).toHaveBeenCalledWith({catalogAppId:o.catalogApps[0].id,deviceUdid:o.devices[1].udid,accountProfileId:`demo-personal-account`,lifecycle:`pending-install`}),await s(xe).toHaveBeenCalledWith({deviceUdid:o.devices[1].udid,bundleId:`com.example.certcountdown`,catalogAppId:o.catalogApps[0].id,accountProfileId:`demo-personal-account`,finishOnboarding:!0}),await s(be.mock.invocationCallOrder[0]).toBeLessThan(xe.mock.invocationCallOrder[0]),await l.click(r.getByRole(`button`,{name:`Install and finish`})),await s(await r.findByText(`Verify on iPhone`)).toBeVisible(),await s(t.getByRole(`heading`,{name:`Install`})).toBeVisible()}},N={name:`First Run - install request error stays inline`,args:{data:C,apiStatus:{..._,baseUrl:`storybook://onboarding-install-error`},initialRoute:`home`,registerPendingAppService:ye,preflightInstallService:async()=>g,installAppService:async()=>{throw Error(`Sideport could not safely start this USB install.`)}},play:async({canvasElement:e})=>{let t=d(e),n=d(t.getByTestId(`runtime-onboarding-panel-app`));await l.click(n.getAllByRole(`radio`)[0]),await l.click(n.getByRole(`button`,{name:/Continue to install/}));let r=d(t.getByTestId(`runtime-onboarding-panel-install`));await s(await r.findByRole(`button`,{name:`Install and finish`})).toBeEnabled(),await l.click(r.getByRole(`button`,{name:`Install and finish`})),await s(await r.findByRole(`alert`)).toHaveTextContent(`Sideport could not safely start this USB install.`),await s(r.getByRole(`button`,{name:`Install and finish`})).toBeEnabled()}},P={name:`First Run - mobile setup at 390px`,args:{data:S,apiStatus:le,initialRoute:`home`},parameters:{viewport:{defaultViewport:`mobile1`}},play:async({canvasElement:e})=>{let t=d(e);await s(t.getByLabelText(`Step 2 of 6: Connect Apple`)).toBeVisible(),await s(t.getByRole(`progressbar`,{name:`Setup progress`})).toBeVisible(),await s(d(t.getByTestId(`runtime-onboarding-panel-apple-signer`)).getByRole(`button`,{name:/Finish Apple setup above/}).getBoundingClientRect().height).toBeGreaterThanOrEqual(44),await s(t.getAllByRole(`main`)).toHaveLength(1)}},F={name:`First Run - server completion survives remount`,args:{data:Ce,apiStatus:y,initialRoute:`home`},play:async({canvasElement:e})=>{let t=d(e);await s(t.getByRole(`heading`,{name:`Apps and iPhones at a glance`})).toBeVisible(),await s(t.queryByRole(`button`,{name:`Onboarding`})).not.toBeInTheDocument()}},I={name:`First Run - verified install retries finalization only`,args:{data:C,apiStatus:fe,initialRoute:`home`,completeOnboardingService:me,readOperationService:async()=>pe},play:async({canvasElement:e})=>{me.mockClear();let t=d(d(e).getByTestId(`runtime-onboarding-panel-install`)),n=await t.findByRole(`button`,{name:`Retry finishing setup`});await s(n).toBeEnabled(),await s(t.getByText(`The app is already verified`)).toBeVisible(),await s(t.queryByRole(`button`,{name:`Installing…`})).not.toBeInTheDocument(),await l.click(n),await s(me).toHaveBeenCalledWith(s.objectContaining({verifiedOperationId:de}))}},L={name:`First Run - unknown install checks iPhone without reinstalling`,args:{data:C,apiStatus:x,initialRoute:`home`,reconcileOperationService:ge,readOperationService:async e=>e===b?he:_e},play:async({canvasElement:e})=>{ge.mockClear();let t=d(d(e).getByTestId(`runtime-onboarding-panel-install`));await s(await t.findByText(`Check before trying again`)).toBeVisible(),await l.click(t.getByRole(`button`,{name:`Check iPhone status`})),await s(ge).toHaveBeenCalledWith(b,s.objectContaining({idempotencyKey:s.stringContaining(`onboarding-reconcile`)})),await s(await t.findByRole(`alert`)).toHaveTextContent(`The installed version does not match the unknown install.`),await s(t.getByRole(`button`,{name:`Check iPhone status`})).toBeEnabled()}},R={name:`First Run - terminal lineage block never offers finalization retry`,args:{data:C,apiStatus:Te,initialRoute:`home`,readOperationService:async()=>Ee},play:async({canvasElement:e})=>{let t=d(d(e).getByTestId(`runtime-onboarding-panel-install`));await s(await t.findByText(`Saved setup evidence no longer matches`)).toBeVisible(),await s(t.queryByRole(`button`,{name:`Retry finishing setup`})).not.toBeInTheDocument(),await s(t.getByRole(`alert`)).toHaveTextContent(`The saved IPA changed after device verification.`)}},Ke=T(`devices`,`Devices - table and mobile cards`),z=T(`device-detail`,`Device detail - two app slots`),B=T(`apps`,`App catalog - Cert Clock seed`),V={name:`Apps - search approved library`,args:{data:o,apiStatus:y,initialRoute:`apps`},play:async({canvasElement:e})=>{let t=d(e),n=t.getByRole(`searchbox`,{name:`Search approved apps`});await l.type(n,`memory`),await s(t.getByRole(`heading`,{name:`Concentration`})).toBeVisible(),await s(t.queryByRole(`heading`,{name:`Cert Clock`})).not.toBeInTheDocument(),await l.clear(n),await s(t.getByRole(`heading`,{name:`Cert Clock`})).toBeVisible()}},H={name:`Install app - one action with smart defaults`,args:{data:Ce,apiStatus:{...y,baseUrl:`storybook://standalone-install`},initialRoute:`install-app`,registerPendingAppService:ye,preflightInstallService:async()=>g,installAppService:async e=>{if(e.finishOnboarding)throw Error(`A signed-in install must not finish onboarding.`);return ve(e)},readOperationService:async e=>({...ve({deviceUdid:o.devices[1].udid,bundleId:`com.example.certcountdown`,catalogAppId:o.catalogApps[0].id,accountProfileId:`demo-personal-account`,preflightId:`install_preflight_story`,planVersion:`sha256:storybook-plan`,finishOnboarding:!1,confirmedPlannedMutations:!0,idempotencyKey:`story`}),operationId:e,status:`running`})},play:async({canvasElement:e})=>{let t=d(e);await s(t.getByRole(`heading`,{name:`Install an app on your iPhone`})).toBeVisible(),await s(t.getByText(`Example Personal Team`)).toBeVisible(),await s(t.getByText(/does not switch this install to Wi-Fi/)).toBeVisible(),await s(t.queryByLabelText(`Apple ID`)).not.toBeInTheDocument(),await s(t.queryByLabelText(`Team ID`)).not.toBeInTheDocument(),await s(t.queryByText(`Server IPA path`)).not.toBeInTheDocument(),await l.click(await t.findByRole(`button`,{name:`Install app`})),await s(await t.findByText(`Sideport is signing, installing, and verifying the app.`)).toBeVisible(),await s(t.getByRole(`button`,{name:`Installing…`})).toBeDisabled(),await s(t.queryByText(`Installed — you can unplug`)).not.toBeInTheDocument()}},U={name:`Install app - active standalone install resumes after reload`,args:{data:Oe,apiStatus:{...y,baseUrl:`storybook://standalone-resume`},initialRoute:`install-app`,registerPendingAppService:c(ye),preflightInstallService:c(async()=>g),readOperationService:c(async()=>({operationId:De,type:`install`,status:`succeeded`,target:{deviceUdid:o.devices[1].udid,bundleId:`com.example.certcountdown`},stages:[{id:`verify`,label:`Verify on iPhone`,status:`succeeded`,message:`Verified after reload.`}],result:{success:!0,bundleId:`com.example.certcountdown`,version:`0.1.0`,expiresAt:`2026-07-19T12:00:00Z`}}))},play:async({canvasElement:e,args:t})=>{let n=d(e);await u(()=>s(t.readOperationService).toHaveBeenCalledWith(De),{timeout:3e3}),await u(()=>s(n.getAllByText(/Installed — you can unplug/).length).toBeGreaterThan(0),{timeout:3e3}),await s(n.getByText(/completion chime was attempted/i)).toBeVisible(),await s(t.registerPendingAppService).not.toHaveBeenCalled()}},W={name:`App catalog - pending registration is not healthy`,args:{data:ke,apiStatus:f,initialRoute:`apps`},play:async({canvasElement:e})=>{let t=d(e).getByText(`Awaiting verified install`);await s(t).toBeVisible();let n=t.closest(`article`);await s(n).not.toBeNull(),await s(d(n).queryByText(`Healthy`)).not.toBeInTheDocument()}},G=T(`activity`,`Activity - operation history`),K=T(`people`,`People - roles, members, invite, audit`),q=T(`settings`,`Settings - sign-in, refresh, signing, and system`),J={name:`Settings - live automatic refresh policy`,args:{data:Ce,apiStatus:y,initialRoute:`settings`,schedulerSettingsService:Ae},play:async({canvasElement:e})=>{Ae.mockClear();let t=d(e);await s(t.getByText(`Every hour · only apps that are due`)).toBeVisible(),await l.click(t.getByRole(`button`,{name:`Turn off automatic refresh`})),await s(Ae).toHaveBeenCalledWith(!1),await s(t.getByRole(`button`,{name:`Turn on automatic refresh`})).toBeVisible(),await s(t.getByText(`Not scheduled`)).toBeVisible()}},Y={args:{data:re,apiStatus:f,initialRoute:`devices`}},X={args:{data:ie,apiStatus:f,initialRoute:`home`}},qe={args:{data:te,apiStatus:oe,initialRoute:`home`}},Je={args:{data:o,apiStatus:p,initialRoute:`settings`}},Ye={name:`Request security - OIDC CSRF stays in memory`,args:{data:o,apiStatus:f,initialRoute:`settings`},play:async()=>{let e=window.fetch,t=[];window.fetch=(async(e,n)=>{let r=typeof e==`string`?e:e instanceof URL?e.toString():e.url;return t.push(new Headers(n?.headers??(e instanceof Request?e.headers:void 0))),new Response(`{}`,{status:200,headers:{"Content-Type":`application/json`,...r.endsWith(`/api/apple-access/personal/status`)?{"X-Sideport-CSRF":`storybook-csrf-token`}:{}}})});try{let e={baseUrl:`/storybook-csrf`,canMutate:!0};await n(e),await i({accountProfileId:`acct_story`,teamId:`TEAMSTORY1`},e),await s(t[1].get(`X-Sideport-CSRF`)).toBe(`storybook-csrf-token`),await s(t[1].get(`Authorization`)).toBeNull(),await i({accountProfileId:`acct_story`,teamId:`TEAMSTORY1`},{...e,token:`storybook-bearer-token`}),await s(t[2].get(`X-Sideport-CSRF`)).toBeNull(),await s(t[2].get(`Authorization`)).toBe(`Bearer storybook-bearer-token`)}finally{window.fetch=e}}},Xe={name:`Command menu - ⌘K search`,args:{data:o,apiStatus:f,initialRoute:`home`,initialCommandOpen:!0}},Z={name:`Device detail - tabs + working refresh`,args:{data:o,apiStatus:f,initialRoute:`device-detail`}},Ze={name:`Signed in - global Add menu + keyboard focus`,args:{data:o,apiStatus:y,initialRoute:`home`},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body),r=t.getByTestId(`global-add-trigger`);await l.click(r);let i=n.getByRole(`group`,{name:`Add to Sideport choices`});await s(i).toBeVisible(),await s(d(i).getByRole(`button`,{name:/Add iPhone/})).toBeVisible(),await s(d(i).getByRole(`button`,{name:/Add app/})).toBeVisible(),await l.keyboard(`{Escape}`),await u(()=>s(r).toHaveFocus())}},Qe={name:`Signed in - Add iPhone waits for Trust automatically`,args:{data:se,apiStatus:f,initialRoute:`devices`,iPhoneSoundPlayer:m},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body);m.mockClear();let r=t.getByRole(`button`,{name:`Add to Sideport`});await l.click(r);let i=n.getByRole(`group`,{name:`Add to Sideport choices`});await l.click(d(i).getByRole(`button`,{name:/Add iPhone/}));let a=n.getByRole(`dialog`,{name:`Add an iPhone`});await s(d(a).getByText(/Developer Mode/)).toBeVisible(),await s(d(a).getAllByRole(`button`,{name:`Connect iPhone`})).toHaveLength(1),await s(d(a).queryByRole(`button`,{name:/Pair|I tapped Trust|Add to Sideport/})).not.toBeInTheDocument(),await l.click(d(a).getByRole(`button`,{name:`Connect iPhone`})),await s(d(a).getByText(`Waiting for Dragos’s iPhone…`)).toBeVisible(),await u(()=>s(d(a).getByText(`Dragos’s iPhone is ready`)).toBeVisible(),{timeout:4e3}),await s(m).toHaveBeenCalledWith(`detected`),await s(d(a).queryByRole(`button`,{name:/Pair|I tapped Trust|Add to Sideport/})).not.toBeInTheDocument(),await l.keyboard(`{Escape}`),await u(()=>s(r).toHaveFocus())}},$e={name:`Signed in - Add app sources + private GitHub permission`,args:{data:o,apiStatus:y,initialRoute:`home`},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body);await l.click(t.getByRole(`button`,{name:`Add to Sideport`}));let r=n.getByRole(`group`,{name:`Add to Sideport choices`});await l.click(d(r).getByRole(`button`,{name:/Add app/}));let i=n.getByRole(`dialog`,{name:`Choose or add an app`});await s(d(i).getByRole(`radio`,{name:/Cert Clock/})).toBeVisible(),await s(d(i).getByRole(`button`,{name:/Choose a file/})).toBeVisible(),await s(d(i).getByRole(`button`,{name:/Sideport storage/})).toBeVisible(),await l.click(d(i).getByRole(`button`,{name:/GitHub release/})),await l.click(d(i).getByText(`Add a repository`)),await l.click(d(i).getByRole(`button`,{name:`Private selected repository`})),await s(d(i).getByText(/Metadata:/)).toBeVisible(),await s(d(i).getByText(/Write access:/)).toBeVisible(),await s(d(i).getByText(/cannot push code, change settings, or read other private repositories/)).toBeVisible();let a=d(i).getByRole(`textbox`,{name:`GitHub repository`});await l.clear(a),await l.type(a,`https://github.com/dragoshont/sideport`),await s(d(i).getByText(`Enter one repository as owner/repository, without a URL.`)).toBeVisible(),await s(d(i).getByRole(`button`,{name:`Continue with GitHub`})).toBeDisabled(),await l.clear(a),await l.type(a,`dragoshont/sideport`),await l.click(d(i).getByRole(`button`,{name:`Continue with GitHub`})),await u(()=>s(d(i).getByText(`GitHub source connected`)).toBeVisible(),{timeout:3e3}),await s(d(i).getByRole(`radio`,{name:/Cert-Clock/})).toBeVisible(),await l.click(d(i).getByRole(`button`,{name:`Import app`})),await u(()=>s(t.getByRole(`heading`,{name:`Install an app on your iPhone`})).toBeVisible()),await s(n.queryByRole(`dialog`,{name:`Add an app`})).not.toBeInTheDocument()}},et={name:`Signed in - global Add at 390px`,args:{data:o,apiStatus:y,initialRoute:`activity`},parameters:{viewport:{defaultViewport:`mobile1`},a11y:{test:`todo`}},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body),r=t.getByRole(`button`,{name:`Add to Sideport`});await s(r).toBeVisible(),await s(r.getBoundingClientRect().height).toBeGreaterThanOrEqual(44),await l.click(r);let i=n.getByRole(`group`,{name:`Add to Sideport choices`});await s(d(i).getByRole(`button`,{name:/Add iPhone/})).toBeVisible(),await s(d(i).getByRole(`button`,{name:/Add app/})).toBeVisible()}},tt={name:`Signed in - live GitHub releases are selectable`,args:{data:o,apiStatus:p,initialRoute:`apps`,addAppServices:je},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body);await l.click(t.getByRole(`button`,{name:`Add to Sideport`}));let r=n.getByRole(`group`,{name:`Add to Sideport choices`});await l.click(d(r).getByRole(`button`,{name:/Add app/}));let i=n.getByRole(`dialog`,{name:`Choose or add an app`});await l.click(d(i).getByRole(`button`,{name:/GitHub release/})),await u(()=>s(d(i).getByRole(`button`,{name:/dragoshont\/sideport/})).toBeVisible()),await l.click(d(i).getByRole(`button`,{name:/dragoshont\/sideport/})),await u(()=>s(d(i).getByRole(`radio`,{name:/Cert-Clock/})).toBeVisible()),await s(d(i).getByRole(`button`,{name:`Import app`})).toBeEnabled()}},nt={name:`Signed in - GitHub remains available when configured storage fails`,args:{data:o,apiStatus:p,initialRoute:`apps`,addAppServices:Me},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body);await l.click(t.getByRole(`button`,{name:`Add to Sideport`})),await l.click(d(n.getByRole(`group`,{name:`Add to Sideport choices`})).getByRole(`button`,{name:/Add app/}));let r=n.getByRole(`dialog`,{name:`Choose or add an app`});await s(await d(r).findByText(/Configured storage is unavailable/)).toBeVisible();let i=d(r).getByRole(`button`,{name:/GitHub release/});await s(i).toBeEnabled(),await l.click(i),await s(await d(r).findByRole(`button`,{name:/dragoshont\/sideport/})).toBeVisible()}},rt={name:`Signed in - live iPhone enrollment polls to accepted`,args:{data:se,apiStatus:p,initialRoute:`devices`,addIPhoneServices:Ne,iPhoneSoundPlayer:m},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body);m.mockClear(),await l.click(t.getByRole(`button`,{name:`Add to Sideport`})),await l.click(d(n.getByRole(`group`,{name:`Add to Sideport choices`})).getByRole(`button`,{name:/Add iPhone/}));let r=n.getByRole(`dialog`,{name:`Add an iPhone`});await l.click(d(r).getByRole(`button`,{name:`Connect iPhone`})),await s(d(r).getByText(`Waiting for Dragos’s iPhone…`)).toBeVisible(),await s(m).toHaveBeenCalledWith(`listening`),await u(()=>s(d(r).getByText(`Dragos’s iPhone is ready`)).toBeVisible(),{timeout:3e3}),await s(m).toHaveBeenCalledWith(`detected`),await l.click(d(r).getByRole(`button`,{name:`Continue`})),await s(t.getByRole(`heading`,{name:`Apps`})).toBeVisible(),await s(n.queryByRole(`dialog`,{name:`Add an iPhone`})).not.toBeInTheDocument()}},it={name:`Signed in - active iPhone enrollment survives close and reopen`,args:{data:o,apiStatus:p,initialRoute:`devices`,addIPhoneServices:Ne},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body),r=async()=>(await l.click(t.getByRole(`button`,{name:`Add to Sideport`})),await l.click(d(n.getByRole(`group`,{name:`Add to Sideport choices`})).getByRole(`button`,{name:/Add iPhone/})),n.getByRole(`dialog`,{name:`Add an iPhone`})),i=await r();await l.click(d(i).getByRole(`button`,{name:`Connect iPhone`})),await l.click(d(i).getByRole(`button`,{name:`Close`}));let a=await r();await s(d(a).getByRole(`button`,{name:`Continue`})).toBeDisabled(),await u(()=>s(d(a).getByText(/iPhone is ready/)).toBeVisible(),{timeout:3e3})}},at={name:`Signed in - iPhone recovery continues automatically with sound cues`,args:{data:o,apiStatus:{...p,baseUrl:`storybook://device-enrollment-recovery`},initialRoute:`devices`,addIPhoneServices:Ue,iPhoneSoundPlayer:m},play:async({canvasElement:e})=>{w.mockClear(),Ve.mockClear(),He.mockClear(),m.mockClear();let t=d(e),n=d(e.ownerDocument.body),r=async()=>(await l.click(t.getByRole(`button`,{name:`Add to Sideport`})),await l.click(d(n.getByRole(`group`,{name:`Add to Sideport choices`})).getByRole(`button`,{name:/Add iPhone/})),n.getByRole(`dialog`,{name:`Add an iPhone`})),i=await r();await l.click(d(i).getByRole(`button`,{name:`Connect iPhone`})),await s(await d(i).findByRole(`button`,{name:`Continue`})).toBeDisabled(),await s(d(i).queryByRole(`button`,{name:`Check Trust and continue`})).not.toBeInTheDocument(),await u(()=>s(Ve).toHaveBeenCalledWith(Be.operationId)),await s(m).toHaveBeenCalledWith(`listening`),await l.click(d(i).getByRole(`button`,{name:`Close`})),await s(d(await r()).getByRole(`button`,{name:`Continue`})).toBeDisabled(),await s(w).toHaveBeenCalledTimes(1)}},ot={name:`Signed in - iPhone recovery retry failure stays verify-only and actionable`,args:{data:o,apiStatus:{...p,baseUrl:`storybook://device-enrollment-recovery-failure`},initialRoute:`devices`,addIPhoneServices:Ge,iPhoneSoundPlayer:m},play:async({canvasElement:e})=>{w.mockClear(),We.mockClear(),m.mockClear();let t=d(e),n=d(e.ownerDocument.body);await l.click(t.getByRole(`button`,{name:`Add to Sideport`})),await l.click(d(n.getByRole(`group`,{name:`Add to Sideport choices`})).getByRole(`button`,{name:/Add iPhone/}));let r=n.getByRole(`dialog`,{name:`Add an iPhone`});await l.click(d(r).getByRole(`button`,{name:`Connect iPhone`})),await u(()=>s(We).toHaveBeenCalledWith(Be.operationId)),await s(d(r).getByRole(`alert`)).toHaveTextContent(`Sideport briefly lost the secure iPhone connection.`),await s(d(r).getByRole(`button`,{name:`Try checking again`})).toBeVisible(),await s(d(r).queryByRole(`button`,{name:/pair/i})).not.toBeInTheDocument(),await s(d(r).queryByRole(`button`,{name:/trust/i})).not.toBeInTheDocument(),await s(w).toHaveBeenCalledTimes(1)}},Q={name:`Signed in - command menu includes Add actions`,args:{data:o,apiStatus:y,initialRoute:`settings`,initialCommandOpen:!0},play:async({canvasElement:e})=>{let t=d(e.ownerDocument.body),n=t.getByRole(`dialog`,{name:`Search and commands`}),r=d(n).getByRole(`button`,{name:/Add iPhone/}),i=d(n).getByRole(`button`,{name:/Add app/});await u(()=>s(r).toBeVisible()),await u(()=>s(i).toBeVisible()),await l.click(r),await s(t.getByRole(`dialog`,{name:`Add an iPhone`})).toBeVisible()}},$={name:`Signed in - Member can add iPhone without Owner controls`,args:{data:ue,apiStatus:y,initialRoute:`settings`},play:async({canvasElement:e})=>{let t=d(e),n=d(e.ownerDocument.body);await s(t.queryByRole(`heading`,{name:`Connect Apple data without over-trusting it`})).not.toBeInTheDocument(),await l.click(t.getByRole(`button`,{name:`Add to Sideport`}));let r=n.getByRole(`group`,{name:`Add to Sideport choices`});await s(d(r).getByRole(`button`,{name:/Add iPhone/})).toBeVisible(),await s(d(r).queryByRole(`button`,{name:/Add app/})).not.toBeInTheDocument()}},E.parameters={...E.parameters,docs:{...E.parameters?.docs,source:{originalSource:`routeStory('home', 'Overview - healthy mixed fleet')`,...E.parameters?.docs?.source}}},D.parameters={...D.parameters,docs:{...D.parameters?.docs,source:{originalSource:`{
  name: 'First Run Onboarding',
  args: {
    data: freshOnboardingData,
    apiStatus: freshOnboardingStatus,
    initialRoute: 'home'
  },
  parameters: {
    docs: {
      description: {
        story: 'The production six-step runtime shell with a deterministic fresh-deployment read model: no Apple signer, accepted iPhone, installation, or scheduler.'
      }
    }
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId('runtime-first-run-onboarding')).toBeVisible();
    await expect(canvas.getByRole('heading', {
      name: 'Connect Apple'
    })).toBeVisible();
    await expect(canvas.getAllByRole('main')).toHaveLength(1);
    await expect(canvas.getByText('1 of 6 complete')).toBeVisible();
  }
}`,...D.parameters?.docs?.source}}},O.parameters={...O.parameters,docs:{...O.parameters?.docs,source:{originalSource:`{
  name: 'Owner account - portal access before iPhone setup',
  args: {
    data: freshOnboardingData,
    apiStatus: freshOnboardingStatus,
    initialRoute: 'home',
    initialSetupOpen: false
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Apps and iPhones at a glance'
    })).toBeVisible();
    await expect(canvas.getByText('Your Owner account is ready')).toBeVisible();
    await expect(canvas.getByText(/Next: Connect Apple/)).toBeVisible();
    await expect(canvas.queryByTestId('runtime-first-run-onboarding')).not.toBeInTheDocument();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Continue setup'
    }));
    await expect(canvas.getByTestId('runtime-first-run-onboarding')).toBeVisible();
    await expect(canvas.getByRole('heading', {
      name: 'Connect Apple'
    })).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Set up later'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Apps and iPhones at a glance'
    })).toBeVisible();
    await expect(canvas.getByRole('button', {
      name: 'Continue setup'
    })).toBeVisible();
  }
}`,...O.parameters?.docs?.source}}},k.parameters={...k.parameters,docs:{...k.parameters?.docs,source:{originalSource:`{
  name: 'First Run - missing iPhone is actionable and automatic',
  args: {
    data: readyForDeviceOnboardingData,
    apiStatus: {
      ...deviceOnboardingStatus,
      mode: 'live'
    },
    initialRoute: 'home',
    addIPhoneServices: onboardingAutoStartServices,
    iPhoneSoundPlayer: iPhoneSoundStory,
    iPhoneAttentionDelayMs: 20
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    startOnboardingIPhoneStory.mockClear();
    iPhoneSoundStory.mockClear();
    const panel = within(canvas.getByTestId('runtime-onboarding-panel-device'));
    await expect(panel.getByRole('heading', {
      name: 'Connect iPhone'
    })).toBeVisible();
    await expect(panel.getByText('Connect the iPhone now')).toBeVisible();
    await expect(panel.getByText('Use a data-capable cable and plug the iPhone directly into the computer running Sideport.')).toBeVisible();
    await expect(panel.getByText('When asked, tap Trust This Computer and enter the iPhone passcode.')).toBeVisible();
    await expect(panel.getByText(/advance automatically/)).toBeVisible();
    await expect(panel.getByRole('button', {
      name: 'Start connecting'
    })).toBeEnabled();
    await expect(panel.queryByRole('button', {
      name: /Pair|I tapped Trust|Add to Sideport/
    })).not.toBeInTheDocument();
    await expect(canvas.getByText('2 of 6 complete')).toBeVisible();
    await userEvent.click(panel.getByRole('button', {
      name: 'Start connecting'
    }));
    const dialog = page.getByRole('dialog', {
      name: 'Add an iPhone'
    });
    await waitFor(() => expect(startOnboardingIPhoneStory).toHaveBeenCalledTimes(1));
    await expect(within(dialog).getByRole('button', {
      name: 'Close Add iPhone'
    })).toHaveFocus();
    await expect(within(dialog).getByRole('button', {
      name: 'Continue'
    })).toBeDisabled();
    await expect(within(dialog).getByText('Waiting for your iPhone…')).toBeVisible();
    await expect(iPhoneSoundStory).toHaveBeenCalledWith('listening');
    await waitFor(() => expect(within(dialog).getByText(/Still waiting/)).toBeVisible());
    await expect(iPhoneSoundStory).toHaveBeenCalledWith('attention');
    await expect(within(dialog).queryByRole('button', {
      name: 'Connect iPhone'
    })).not.toBeInTheDocument();
    await new Promise(resolve => window.setTimeout(resolve, 1_200));
    await expect(startOnboardingIPhoneStory).toHaveBeenCalledTimes(1);
  }
}`,...k.parameters?.docs?.source}}},A.parameters={...A.parameters,docs:{...A.parameters?.docs,source:{originalSource:`{
  name: 'First Run - active iPhone connection resumes without another start',
  args: {
    data: readyForDeviceOnboardingData,
    apiStatus: deviceEnrollmentInProgressStatus,
    initialRoute: 'home',
    addIPhoneServices: onboardingResumeServices
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    startResumedOnboardingIPhoneStory.mockClear();
    readResumedOnboardingIPhoneStory.mockClear();
    await userEvent.click(within(canvas.getByTestId('runtime-onboarding-panel-device')).getByRole('button', {
      name: 'Show connection status'
    }));
    const dialog = page.getByRole('dialog', {
      name: 'Add an iPhone'
    });
    await waitFor(() => expect(readResumedOnboardingIPhoneStory).toHaveBeenCalled());
    await expect(startResumedOnboardingIPhoneStory).not.toHaveBeenCalled();
    await expect(within(dialog).getByRole('button', {
      name: 'Continue'
    })).toBeDisabled();
  }
}`,...A.parameters?.docs?.source}}},j.parameters={...j.parameters,docs:{...j.parameters?.docs,source:{originalSource:`{
  name: 'First Run - selection survives a runtime remount',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: interactiveOnboardingStatus,
    initialRoute: 'home'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByTestId('runtime-first-run-onboarding')).toBeVisible();
    const setupNavigation = canvas.getByRole('navigation', {
      name: 'First-run setup steps'
    });
    await expect(setupNavigation).toBeVisible();
    await expect(within(setupNavigation).getByRole('button', {
      name: /Check Sideport/
    })).toBeVisible();
    await expect(within(setupNavigation).getByRole('button', {
      name: /Connect Apple/
    })).toBeVisible();
    await expect(within(setupNavigation).getByRole('button', {
      name: /Connect iPhone/
    })).toBeVisible();
    await expect(within(setupNavigation).getByRole('button', {
      name: /Choose app/
    })).toBeVisible();
    await expect(within(setupNavigation).getByRole('button', {
      name: /Install/
    })).toBeVisible();
    await expect(within(setupNavigation).getByRole('button', {
      name: /Ready/
    })).toBeVisible();
    const appPanel = within(canvas.getByTestId('runtime-onboarding-panel-app'));
    const radios = appPanel.getAllByRole('radio');
    await userEvent.click(radios[0]);
    await userEvent.keyboard('{ArrowDown}');
    await expect(radios[1]).toBeChecked();
    await expect(canvas.getByTestId('runtime-onboarding-live-region')).toHaveTextContent('Dice Roll selected');
    await userEvent.click(canvas.getByRole('button', {
      name: 'Show technical details'
    }));
    await expect(canvas.getByText('Browser-session app choice · non-authoritative')).toBeVisible();
  }
}`,...j.parameters?.docs?.source}}},M.parameters={...M.parameters,docs:{...M.parameters?.docs,source:{originalSource:`{
  name: 'First Run - install starts inline once',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: {
      ...interactiveOnboardingStatus,
      baseUrl: 'storybook://onboarding-install-start'
    },
    initialRoute: 'home',
    registerPendingAppService: savePendingOnboardingStory,
    preflightInstallService: preflightOnboardingStory,
    installAppService: async payload => {
      if (!payload.finishOnboarding) throw new Error('The onboarding install must carry finishOnboarding=true.');
      if (payload.preflightId !== installPreflightReady.preflightId || payload.planVersion !== installPreflightReady.planVersion) throw new Error('The confirmed preflight was not submitted.');
      if (payload.catalogAppId !== fixtures.catalogApps[0].id || payload.accountProfileId !== 'demo-personal-account') throw new Error('The selected catalog app and Apple account were not bound to the install.');
      return installStarted(payload);
    },
    readOperationService: async operationId => ({
      ...installStarted({
        deviceUdid: fixtures.devices[1].udid,
        bundleId: 'com.example.certcountdown',
        catalogAppId: fixtures.catalogApps[0].id,
        accountProfileId: 'demo-personal-account',
        preflightId: 'install_preflight_story',
        planVersion: 'sha256:storybook-plan',
        finishOnboarding: true,
        confirmedPlannedMutations: true,
        idempotencyKey: 'story'
      }),
      operationId,
      status: 'succeeded',
      stages: [{
        id: 'verify',
        label: 'Verify on iPhone',
        status: 'succeeded',
        message: 'Verified.'
      }]
    })
  },
  play: async ({
    canvasElement
  }) => {
    savePendingOnboardingStory.mockClear();
    preflightOnboardingStory.mockClear();
    const canvas = within(canvasElement);
    const appPanel = within(canvas.getByTestId('runtime-onboarding-panel-app'));
    await userEvent.click(appPanel.getAllByRole('radio')[0]);
    await userEvent.click(appPanel.getByRole('button', {
      name: /Continue to install/
    }));
    const installPanel = within(canvas.getByTestId('runtime-onboarding-panel-install'));
    await expect(await installPanel.findByRole('button', {
      name: 'Install and finish'
    })).toBeEnabled();
    await expect(savePendingOnboardingStory).toHaveBeenCalledWith({
      catalogAppId: fixtures.catalogApps[0].id,
      deviceUdid: fixtures.devices[1].udid,
      accountProfileId: 'demo-personal-account',
      lifecycle: 'pending-install'
    });
    await expect(preflightOnboardingStory).toHaveBeenCalledWith({
      deviceUdid: fixtures.devices[1].udid,
      bundleId: 'com.example.certcountdown',
      catalogAppId: fixtures.catalogApps[0].id,
      accountProfileId: 'demo-personal-account',
      finishOnboarding: true
    });
    await expect(savePendingOnboardingStory.mock.invocationCallOrder[0]).toBeLessThan(preflightOnboardingStory.mock.invocationCallOrder[0]);
    await userEvent.click(installPanel.getByRole('button', {
      name: 'Install and finish'
    }));
    await expect(await installPanel.findByText('Verify on iPhone')).toBeVisible();
    await expect(canvas.getByRole('heading', {
      name: 'Install'
    })).toBeVisible();
  }
}`,...M.parameters?.docs?.source}}},N.parameters={...N.parameters,docs:{...N.parameters?.docs,source:{originalSource:`{
  name: 'First Run - install request error stays inline',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: {
      ...interactiveOnboardingStatus,
      baseUrl: 'storybook://onboarding-install-error'
    },
    initialRoute: 'home',
    registerPendingAppService: savePendingStoryApp,
    preflightInstallService: async () => installPreflightReady,
    installAppService: async () => {
      throw new Error('Sideport could not safely start this USB install.');
    }
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const appPanel = within(canvas.getByTestId('runtime-onboarding-panel-app'));
    await userEvent.click(appPanel.getAllByRole('radio')[0]);
    await userEvent.click(appPanel.getByRole('button', {
      name: /Continue to install/
    }));
    const installPanel = within(canvas.getByTestId('runtime-onboarding-panel-install'));
    await expect(await installPanel.findByRole('button', {
      name: 'Install and finish'
    })).toBeEnabled();
    await userEvent.click(installPanel.getByRole('button', {
      name: 'Install and finish'
    }));
    await expect(await installPanel.findByRole('alert')).toHaveTextContent('Sideport could not safely start this USB install.');
    await expect(installPanel.getByRole('button', {
      name: 'Install and finish'
    })).toBeEnabled();
  }
}`,...N.parameters?.docs?.source}}},P.parameters={...P.parameters,docs:{...P.parameters?.docs,source:{originalSource:`{
  name: 'First Run - mobile setup at 390px',
  args: {
    data: freshOnboardingData,
    apiStatus: freshOnboardingStatus,
    initialRoute: 'home'
  },
  parameters: {
    viewport: {
      defaultViewport: 'mobile1'
    }
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByLabelText('Step 2 of 6: Connect Apple')).toBeVisible();
    await expect(canvas.getByRole('progressbar', {
      name: 'Setup progress'
    })).toBeVisible();
    const primary = within(canvas.getByTestId('runtime-onboarding-panel-apple-signer')).getByRole('button', {
      name: /Finish Apple setup above/
    });
    await expect(primary.getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
    await expect(canvas.getAllByRole('main')).toHaveLength(1);
  }
}`,...P.parameters?.docs?.source}}},F.parameters={...F.parameters,docs:{...F.parameters?.docs,source:{originalSource:`{
  name: 'First Run - server completion survives remount',
  args: {
    data: completedOnboardingData,
    apiStatus: completedOnboardingStatus,
    initialRoute: 'home'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Apps and iPhones at a glance'
    })).toBeVisible();
    await expect(canvas.queryByRole('button', {
      name: 'Onboarding'
    })).not.toBeInTheDocument();
  }
}`,...F.parameters?.docs?.source}}},I.parameters={...I.parameters,docs:{...I.parameters?.docs,source:{originalSource:`{
  name: 'First Run - verified install retries finalization only',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: finalizationRecoveryStatus,
    initialRoute: 'home',
    completeOnboardingService: finishOnboardingStory,
    readOperationService: async () => finalizationWaitingOperation
  },
  play: async ({
    canvasElement
  }) => {
    finishOnboardingStory.mockClear();
    const canvas = within(canvasElement);
    const installPanel = within(canvas.getByTestId('runtime-onboarding-panel-install'));
    const retry = await installPanel.findByRole('button', {
      name: 'Retry finishing setup'
    });
    await expect(retry).toBeEnabled();
    await expect(installPanel.getByText('The app is already verified')).toBeVisible();
    await expect(installPanel.queryByRole('button', {
      name: 'Installing…'
    })).not.toBeInTheDocument();
    await userEvent.click(retry);
    await expect(finishOnboardingStory).toHaveBeenCalledWith(expect.objectContaining({
      verifiedOperationId: finalizationOperationId
    }));
  }
}`,...I.parameters?.docs?.source}}},L.parameters={...L.parameters,docs:{...L.parameters?.docs,source:{originalSource:`{
  name: 'First Run - unknown install checks iPhone without reinstalling',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: unknownInstallStatus,
    initialRoute: 'home',
    reconcileOperationService: reconcileOnboardingStory,
    readOperationService: async operationId => operationId === unknownInstallOperationId ? unknownInstallOperation : reconciledMismatchOperation
  },
  play: async ({
    canvasElement
  }) => {
    reconcileOnboardingStory.mockClear();
    const canvas = within(canvasElement);
    const installPanel = within(canvas.getByTestId('runtime-onboarding-panel-install'));
    await expect(await installPanel.findByText('Check before trying again')).toBeVisible();
    await userEvent.click(installPanel.getByRole('button', {
      name: 'Check iPhone status'
    }));
    await expect(reconcileOnboardingStory).toHaveBeenCalledWith(unknownInstallOperationId, expect.objectContaining({
      idempotencyKey: expect.stringContaining('onboarding-reconcile')
    }));
    await expect(await installPanel.findByRole('alert')).toHaveTextContent('The installed version does not match the unknown install.');
    await expect(installPanel.getByRole('button', {
      name: 'Check iPhone status'
    })).toBeEnabled();
  }
}`,...L.parameters?.docs?.source}}},R.parameters={...R.parameters,docs:{...R.parameters?.docs,source:{originalSource:`{
  name: 'First Run - terminal lineage block never offers finalization retry',
  args: {
    data: readyForAppOnboardingData,
    apiStatus: terminalLineageStatus,
    initialRoute: 'home',
    readOperationService: async () => terminalLineageOperation
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const installPanel = within(canvas.getByTestId('runtime-onboarding-panel-install'));
    await expect(await installPanel.findByText('Saved setup evidence no longer matches')).toBeVisible();
    await expect(installPanel.queryByRole('button', {
      name: 'Retry finishing setup'
    })).not.toBeInTheDocument();
    await expect(installPanel.getByRole('alert')).toHaveTextContent('The saved IPA changed after device verification.');
  }
}`,...R.parameters?.docs?.source}}},Ke.parameters={...Ke.parameters,docs:{...Ke.parameters?.docs,source:{originalSource:`routeStory('devices', 'Devices - table and mobile cards')`,...Ke.parameters?.docs?.source}}},z.parameters={...z.parameters,docs:{...z.parameters?.docs,source:{originalSource:`routeStory('device-detail', 'Device detail - two app slots')`,...z.parameters?.docs?.source}}},B.parameters={...B.parameters,docs:{...B.parameters?.docs,source:{originalSource:`routeStory('apps', 'App catalog - Cert Clock seed')`,...B.parameters?.docs?.source}}},V.parameters={...V.parameters,docs:{...V.parameters?.docs,source:{originalSource:`{
  name: 'Apps - search approved library',
  args: {
    data: fixtures,
    apiStatus: completedOnboardingStatus,
    initialRoute: 'apps'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const search = canvas.getByRole('searchbox', {
      name: 'Search approved apps'
    });
    await userEvent.type(search, 'memory');
    await expect(canvas.getByRole('heading', {
      name: 'Concentration'
    })).toBeVisible();
    await expect(canvas.queryByRole('heading', {
      name: 'Cert Clock'
    })).not.toBeInTheDocument();
    await userEvent.clear(search);
    await expect(canvas.getByRole('heading', {
      name: 'Cert Clock'
    })).toBeVisible();
  }
}`,...V.parameters?.docs?.source}}},H.parameters={...H.parameters,docs:{...H.parameters?.docs,source:{originalSource:`{
  name: 'Install app - one action with smart defaults',
  args: {
    data: completedOnboardingData,
    apiStatus: {
      ...completedOnboardingStatus,
      baseUrl: 'storybook://standalone-install'
    },
    initialRoute: 'install-app',
    registerPendingAppService: savePendingStoryApp,
    preflightInstallService: async () => installPreflightReady,
    installAppService: async payload => {
      if (payload.finishOnboarding) throw new Error('A signed-in install must not finish onboarding.');
      return installStarted(payload);
    },
    readOperationService: async operationId => ({
      ...installStarted({
        deviceUdid: fixtures.devices[1].udid,
        bundleId: 'com.example.certcountdown',
        catalogAppId: fixtures.catalogApps[0].id,
        accountProfileId: 'demo-personal-account',
        preflightId: 'install_preflight_story',
        planVersion: 'sha256:storybook-plan',
        finishOnboarding: false,
        confirmedPlannedMutations: true,
        idempotencyKey: 'story'
      }),
      operationId,
      status: 'running'
    })
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Install an app on your iPhone'
    })).toBeVisible();
    await expect(canvas.getByText('Example Personal Team')).toBeVisible();
    await expect(canvas.getByText(/does not switch this install to Wi-Fi/)).toBeVisible();
    await expect(canvas.queryByLabelText('Apple ID')).not.toBeInTheDocument();
    await expect(canvas.queryByLabelText('Team ID')).not.toBeInTheDocument();
    await expect(canvas.queryByText('Server IPA path')).not.toBeInTheDocument();
    await userEvent.click(await canvas.findByRole('button', {
      name: 'Install app'
    }));
    await expect(await canvas.findByText('Sideport is signing, installing, and verifying the app.')).toBeVisible();
    await expect(canvas.getByRole('button', {
      name: 'Installing…'
    })).toBeDisabled();
    await expect(canvas.queryByText('Installed — you can unplug')).not.toBeInTheDocument();
  }
}`,...H.parameters?.docs?.source}}},U.parameters={...U.parameters,docs:{...U.parameters?.docs,source:{originalSource:`{
  name: 'Install app - active standalone install resumes after reload',
  args: {
    data: standaloneResumeData,
    apiStatus: {
      ...completedOnboardingStatus,
      baseUrl: 'storybook://standalone-resume'
    },
    initialRoute: 'install-app',
    registerPendingAppService: fn(savePendingStoryApp),
    preflightInstallService: fn(async () => installPreflightReady),
    readOperationService: fn(async () => ({
      operationId: standaloneResumeOperationId,
      type: 'install',
      status: 'succeeded',
      target: {
        deviceUdid: fixtures.devices[1].udid,
        bundleId: 'com.example.certcountdown'
      },
      stages: [{
        id: 'verify',
        label: 'Verify on iPhone',
        status: 'succeeded',
        message: 'Verified after reload.'
      }],
      result: {
        success: true,
        bundleId: 'com.example.certcountdown',
        version: '0.1.0',
        expiresAt: '2026-07-19T12:00:00Z'
      }
    }))
  },
  play: async ({
    canvasElement,
    args
  }) => {
    const canvas = within(canvasElement);
    await waitFor(() => expect(args.readOperationService).toHaveBeenCalledWith(standaloneResumeOperationId), {
      timeout: 3_000
    });
    await waitFor(() => expect(canvas.getAllByText(/Installed — you can unplug/).length).toBeGreaterThan(0), {
      timeout: 3_000
    });
    await expect(canvas.getByText(/completion chime was attempted/i)).toBeVisible();
    await expect(args.registerPendingAppService).not.toHaveBeenCalled();
  }
}`,...U.parameters?.docs?.source}}},W.parameters={...W.parameters,docs:{...W.parameters?.docs,source:{originalSource:`{
  name: 'App catalog - pending registration is not healthy',
  args: {
    data: pendingRegistrationData,
    apiStatus: demoStatus,
    initialRoute: 'apps'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const pendingLabel = canvas.getByText('Awaiting verified install');
    await expect(pendingLabel).toBeVisible();
    const registration = pendingLabel.closest('article');
    await expect(registration).not.toBeNull();
    await expect(within(registration!).queryByText('Healthy')).not.toBeInTheDocument();
  }
}`,...W.parameters?.docs?.source}}},G.parameters={...G.parameters,docs:{...G.parameters?.docs,source:{originalSource:`routeStory('activity', 'Activity - operation history')`,...G.parameters?.docs?.source}}},K.parameters={...K.parameters,docs:{...K.parameters?.docs,source:{originalSource:`routeStory('people', 'People - roles, members, invite, audit')`,...K.parameters?.docs?.source}}},q.parameters={...q.parameters,docs:{...q.parameters?.docs,source:{originalSource:`routeStory('settings', 'Settings - sign-in, refresh, signing, and system')`,...q.parameters?.docs?.source}}},J.parameters={...J.parameters,docs:{...J.parameters?.docs,source:{originalSource:`{
  name: 'Settings - live automatic refresh policy',
  args: {
    data: completedOnboardingData,
    apiStatus: completedOnboardingStatus,
    initialRoute: 'settings',
    schedulerSettingsService: schedulerDisabledStory
  },
  play: async ({
    canvasElement
  }) => {
    schedulerDisabledStory.mockClear();
    const canvas = within(canvasElement);
    await expect(canvas.getByText('Every hour · only apps that are due')).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Turn off automatic refresh'
    }));
    await expect(schedulerDisabledStory).toHaveBeenCalledWith(false);
    await expect(canvas.getByRole('button', {
      name: 'Turn on automatic refresh'
    })).toBeVisible();
    await expect(canvas.getByText('Not scheduled')).toBeVisible();
  }
}`,...J.parameters?.docs?.source}}},Y.parameters={...Y.parameters,docs:{...Y.parameters?.docs,source:{originalSource:`{
  args: {
    data: emptyFixtures,
    apiStatus: demoStatus,
    initialRoute: 'devices'
  }
}`,...Y.parameters?.docs?.source}}},X.parameters={...X.parameters,docs:{...X.parameters?.docs,source:{originalSource:`{
  args: {
    data: blockedFixtures,
    apiStatus: demoStatus,
    initialRoute: 'home'
  }
}`,...X.parameters?.docs?.source}}},qe.parameters={...qe.parameters,docs:{...qe.parameters?.docs,source:{originalSource:`{
  args: {
    data: runtimeEmptyData,
    apiStatus: apiUnavailableStatus,
    initialRoute: 'home'
  }
}`,...qe.parameters?.docs?.source}}},Je.parameters={...Je.parameters,docs:{...Je.parameters?.docs,source:{originalSource:`{
  args: {
    data: fixtures,
    apiStatus: tokenRequiredStatus,
    initialRoute: 'settings'
  }
}`,...Je.parameters?.docs?.source}}},Ye.parameters={...Ye.parameters,docs:{...Ye.parameters?.docs,source:{originalSource:`{
  name: 'Request security - OIDC CSRF stays in memory',
  args: {
    data: fixtures,
    apiStatus: demoStatus,
    initialRoute: 'settings'
  },
  play: async () => {
    const originalFetch = window.fetch;
    const requestHeaders: Headers[] = [];
    window.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      requestHeaders.push(new Headers(init?.headers ?? (input instanceof Request ? input.headers : undefined)));
      return new Response('{}', {
        status: 200,
        headers: {
          'Content-Type': 'application/json',
          ...(url.endsWith('/api/apple-access/personal/status') ? {
            'X-Sideport-CSRF': 'storybook-csrf-token'
          } : {})
        }
      });
    }) as typeof window.fetch;
    try {
      const oidcConfig = {
        baseUrl: '/storybook-csrf',
        canMutate: true
      };
      await refreshPersonalAppleRequestSecurity(oidcConfig);
      await selectPersonalAppleTeam({
        accountProfileId: 'acct_story',
        teamId: 'TEAMSTORY1'
      }, oidcConfig);
      await expect(requestHeaders[1].get('X-Sideport-CSRF')).toBe('storybook-csrf-token');
      await expect(requestHeaders[1].get('Authorization')).toBeNull();
      await selectPersonalAppleTeam({
        accountProfileId: 'acct_story',
        teamId: 'TEAMSTORY1'
      }, {
        ...oidcConfig,
        token: 'storybook-bearer-token'
      });
      await expect(requestHeaders[2].get('X-Sideport-CSRF')).toBeNull();
      await expect(requestHeaders[2].get('Authorization')).toBe('Bearer storybook-bearer-token');
    } finally {
      window.fetch = originalFetch;
    }
  }
}`,...Ye.parameters?.docs?.source}}},Xe.parameters={...Xe.parameters,docs:{...Xe.parameters?.docs,source:{originalSource:`{
  name: 'Command menu - ⌘K search',
  args: {
    data: fixtures,
    apiStatus: demoStatus,
    initialRoute: 'home',
    initialCommandOpen: true
  }
}`,...Xe.parameters?.docs?.source}}},Z.parameters={...Z.parameters,docs:{...Z.parameters?.docs,source:{originalSource:`{
  name: 'Device detail - tabs + working refresh',
  args: {
    data: fixtures,
    apiStatus: demoStatus,
    initialRoute: 'device-detail'
  }
}`,...Z.parameters?.docs?.source}}},Ze.parameters={...Ze.parameters,docs:{...Ze.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - global Add menu + keyboard focus',
  args: {
    data: fixtures,
    apiStatus: completedOnboardingStatus,
    initialRoute: 'home'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    const trigger = canvas.getByTestId('global-add-trigger');
    await userEvent.click(trigger);
    const addChoices = page.getByRole('group', {
      name: 'Add to Sideport choices'
    });
    await expect(addChoices).toBeVisible();
    await expect(within(addChoices).getByRole('button', {
      name: /Add iPhone/
    })).toBeVisible();
    await expect(within(addChoices).getByRole('button', {
      name: /Add app/
    })).toBeVisible();
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(trigger).toHaveFocus());
  }
}`,...Ze.parameters?.docs?.source}}},Qe.parameters={...Qe.parameters,docs:{...Qe.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - Add iPhone waits for Trust automatically',
  args: {
    data: dragosFixtures,
    apiStatus: demoStatus,
    initialRoute: 'devices',
    iPhoneSoundPlayer: iPhoneSoundStory
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    iPhoneSoundStory.mockClear();
    const globalTrigger = canvas.getByRole('button', {
      name: 'Add to Sideport'
    });
    await userEvent.click(globalTrigger);
    const addChoices = page.getByRole('group', {
      name: 'Add to Sideport choices'
    });
    await userEvent.click(within(addChoices).getByRole('button', {
      name: /Add iPhone/
    }));
    const dialog = page.getByRole('dialog', {
      name: 'Add an iPhone'
    });
    await expect(within(dialog).getByText(/Developer Mode/)).toBeVisible();
    await expect(within(dialog).getAllByRole('button', {
      name: 'Connect iPhone'
    })).toHaveLength(1);
    await expect(within(dialog).queryByRole('button', {
      name: /Pair|I tapped Trust|Add to Sideport/
    })).not.toBeInTheDocument();
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Connect iPhone'
    }));
    await expect(within(dialog).getByText('Waiting for Dragos’s iPhone…')).toBeVisible();
    await waitFor(() => expect(within(dialog).getByText('Dragos’s iPhone is ready')).toBeVisible(), {
      timeout: 4_000
    });
    await expect(iPhoneSoundStory).toHaveBeenCalledWith('detected');
    await expect(within(dialog).queryByRole('button', {
      name: /Pair|I tapped Trust|Add to Sideport/
    })).not.toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(globalTrigger).toHaveFocus());
  }
}`,...Qe.parameters?.docs?.source}}},$e.parameters={...$e.parameters,docs:{...$e.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - Add app sources + private GitHub permission',
  args: {
    data: fixtures,
    apiStatus: completedOnboardingStatus,
    initialRoute: 'home'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    await userEvent.click(canvas.getByRole('button', {
      name: 'Add to Sideport'
    }));
    const addChoices = page.getByRole('group', {
      name: 'Add to Sideport choices'
    });
    await userEvent.click(within(addChoices).getByRole('button', {
      name: /Add app/
    }));
    const dialog = page.getByRole('dialog', {
      name: 'Choose or add an app'
    });
    await expect(within(dialog).getByRole('radio', {
      name: /Cert Clock/
    })).toBeVisible();
    await expect(within(dialog).getByRole('button', {
      name: /Choose a file/
    })).toBeVisible();
    await expect(within(dialog).getByRole('button', {
      name: /Sideport storage/
    })).toBeVisible();
    await userEvent.click(within(dialog).getByRole('button', {
      name: /GitHub release/
    }));
    await userEvent.click(within(dialog).getByText('Add a repository'));
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Private selected repository'
    }));
    await expect(within(dialog).getByText(/Metadata:/)).toBeVisible();
    await expect(within(dialog).getByText(/Write access:/)).toBeVisible();
    await expect(within(dialog).getByText(/cannot push code, change settings, or read other private repositories/)).toBeVisible();
    const repository = within(dialog).getByRole('textbox', {
      name: 'GitHub repository'
    });
    await userEvent.clear(repository);
    await userEvent.type(repository, 'https://github.com/dragoshont/sideport');
    await expect(within(dialog).getByText('Enter one repository as owner/repository, without a URL.')).toBeVisible();
    await expect(within(dialog).getByRole('button', {
      name: 'Continue with GitHub'
    })).toBeDisabled();
    await userEvent.clear(repository);
    await userEvent.type(repository, 'dragoshont/sideport');
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Continue with GitHub'
    }));
    await waitFor(() => expect(within(dialog).getByText('GitHub source connected')).toBeVisible(), {
      timeout: 3_000
    });
    await expect(within(dialog).getByRole('radio', {
      name: /Cert-Clock/
    })).toBeVisible();
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Import app'
    }));
    await waitFor(() => expect(canvas.getByRole('heading', {
      name: 'Install an app on your iPhone'
    })).toBeVisible());
    await expect(page.queryByRole('dialog', {
      name: 'Add an app'
    })).not.toBeInTheDocument();
  }
}`,...$e.parameters?.docs?.source}}},et.parameters={...et.parameters,docs:{...et.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - global Add at 390px',
  args: {
    data: fixtures,
    apiStatus: completedOnboardingStatus,
    initialRoute: 'activity'
  },
  parameters: {
    viewport: {
      defaultViewport: 'mobile1'
    },
    a11y: {
      test: 'todo'
    }
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    const trigger = canvas.getByRole('button', {
      name: 'Add to Sideport'
    });
    await expect(trigger).toBeVisible();
    await expect(trigger.getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
    await userEvent.click(trigger);
    const addChoices = page.getByRole('group', {
      name: 'Add to Sideport choices'
    });
    await expect(within(addChoices).getByRole('button', {
      name: /Add iPhone/
    })).toBeVisible();
    await expect(within(addChoices).getByRole('button', {
      name: /Add app/
    })).toBeVisible();
  }
}`,...et.parameters?.docs?.source}}},tt.parameters={...tt.parameters,docs:{...tt.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - live GitHub releases are selectable',
  args: {
    data: fixtures,
    apiStatus: tokenRequiredStatus,
    initialRoute: 'apps',
    addAppServices: runtimeAddAppServices
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    await userEvent.click(canvas.getByRole('button', {
      name: 'Add to Sideport'
    }));
    const addChoices = page.getByRole('group', {
      name: 'Add to Sideport choices'
    });
    await userEvent.click(within(addChoices).getByRole('button', {
      name: /Add app/
    }));
    const dialog = page.getByRole('dialog', {
      name: 'Choose or add an app'
    });
    await userEvent.click(within(dialog).getByRole('button', {
      name: /GitHub release/
    }));
    await waitFor(() => expect(within(dialog).getByRole('button', {
      name: /dragoshont\\/sideport/
    })).toBeVisible());
    await userEvent.click(within(dialog).getByRole('button', {
      name: /dragoshont\\/sideport/
    }));
    await waitFor(() => expect(within(dialog).getByRole('radio', {
      name: /Cert-Clock/
    })).toBeVisible());
    await expect(within(dialog).getByRole('button', {
      name: 'Import app'
    })).toBeEnabled();
  }
}`,...tt.parameters?.docs?.source}}},nt.parameters={...nt.parameters,docs:{...nt.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - GitHub remains available when configured storage fails',
  args: {
    data: fixtures,
    apiStatus: tokenRequiredStatus,
    initialRoute: 'apps',
    addAppServices: githubAvailableWhenStorageFails
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    await userEvent.click(canvas.getByRole('button', {
      name: 'Add to Sideport'
    }));
    await userEvent.click(within(page.getByRole('group', {
      name: 'Add to Sideport choices'
    })).getByRole('button', {
      name: /Add app/
    }));
    const dialog = page.getByRole('dialog', {
      name: 'Choose or add an app'
    });
    await expect(await within(dialog).findByText(/Configured storage is unavailable/)).toBeVisible();
    const github = within(dialog).getByRole('button', {
      name: /GitHub release/
    });
    await expect(github).toBeEnabled();
    await userEvent.click(github);
    await expect(await within(dialog).findByRole('button', {
      name: /dragoshont\\/sideport/
    })).toBeVisible();
  }
}`,...nt.parameters?.docs?.source}}},rt.parameters={...rt.parameters,docs:{...rt.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - live iPhone enrollment polls to accepted',
  args: {
    data: dragosFixtures,
    apiStatus: tokenRequiredStatus,
    initialRoute: 'devices',
    addIPhoneServices: runtimeAddIPhoneServices,
    iPhoneSoundPlayer: iPhoneSoundStory
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    iPhoneSoundStory.mockClear();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Add to Sideport'
    }));
    await userEvent.click(within(page.getByRole('group', {
      name: 'Add to Sideport choices'
    })).getByRole('button', {
      name: /Add iPhone/
    }));
    const dialog = page.getByRole('dialog', {
      name: 'Add an iPhone'
    });
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Connect iPhone'
    }));
    await expect(within(dialog).getByText('Waiting for Dragos’s iPhone…')).toBeVisible();
    await expect(iPhoneSoundStory).toHaveBeenCalledWith('listening');
    await waitFor(() => expect(within(dialog).getByText('Dragos’s iPhone is ready')).toBeVisible(), {
      timeout: 3_000
    });
    await expect(iPhoneSoundStory).toHaveBeenCalledWith('detected');
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Continue'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Apps'
    })).toBeVisible();
    await expect(page.queryByRole('dialog', {
      name: 'Add an iPhone'
    })).not.toBeInTheDocument();
  }
}`,...rt.parameters?.docs?.source}}},it.parameters={...it.parameters,docs:{...it.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - active iPhone enrollment survives close and reopen',
  args: {
    data: fixtures,
    apiStatus: tokenRequiredStatus,
    initialRoute: 'devices',
    addIPhoneServices: runtimeAddIPhoneServices
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    const openDialog = async () => {
      await userEvent.click(canvas.getByRole('button', {
        name: 'Add to Sideport'
      }));
      await userEvent.click(within(page.getByRole('group', {
        name: 'Add to Sideport choices'
      })).getByRole('button', {
        name: /Add iPhone/
      }));
      return page.getByRole('dialog', {
        name: 'Add an iPhone'
      });
    };
    const firstDialog = await openDialog();
    await userEvent.click(within(firstDialog).getByRole('button', {
      name: 'Connect iPhone'
    }));
    await userEvent.click(within(firstDialog).getByRole('button', {
      name: 'Close'
    }));
    const resumedDialog = await openDialog();
    await expect(within(resumedDialog).getByRole('button', {
      name: 'Continue'
    })).toBeDisabled();
    await waitFor(() => expect(within(resumedDialog).getByText(/iPhone is ready/)).toBeVisible(), {
      timeout: 3_000
    });
  }
}`,...it.parameters?.docs?.source}}},at.parameters={...at.parameters,docs:{...at.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - iPhone recovery continues automatically with sound cues',
  args: {
    data: fixtures,
    apiStatus: {
      ...tokenRequiredStatus,
      baseUrl: 'storybook://device-enrollment-recovery'
    },
    initialRoute: 'devices',
    addIPhoneServices: enrollmentRecoveryServices,
    iPhoneSoundPlayer: iPhoneSoundStory
  },
  play: async ({
    canvasElement
  }) => {
    startEnrollmentRecoveryStory.mockClear();
    retryEnrollmentRecoveryStory.mockClear();
    readEnrollmentRecoveryStory.mockClear();
    iPhoneSoundStory.mockClear();
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    const openDialog = async () => {
      await userEvent.click(canvas.getByRole('button', {
        name: 'Add to Sideport'
      }));
      await userEvent.click(within(page.getByRole('group', {
        name: 'Add to Sideport choices'
      })).getByRole('button', {
        name: /Add iPhone/
      }));
      return page.getByRole('dialog', {
        name: 'Add an iPhone'
      });
    };
    const firstDialog = await openDialog();
    await userEvent.click(within(firstDialog).getByRole('button', {
      name: 'Connect iPhone'
    }));
    const continueButton = await within(firstDialog).findByRole('button', {
      name: 'Continue'
    });
    await expect(continueButton).toBeDisabled();
    await expect(within(firstDialog).queryByRole('button', {
      name: 'Check Trust and continue'
    })).not.toBeInTheDocument();
    await waitFor(() => expect(retryEnrollmentRecoveryStory).toHaveBeenCalledWith(enrollmentRecoverySource.operationId));
    await expect(iPhoneSoundStory).toHaveBeenCalledWith('listening');
    await userEvent.click(within(firstDialog).getByRole('button', {
      name: 'Close'
    }));
    const resumedDialog = await openDialog();
    await expect(within(resumedDialog).getByRole('button', {
      name: 'Continue'
    })).toBeDisabled();
    await expect(startEnrollmentRecoveryStory).toHaveBeenCalledTimes(1);
  }
}`,...at.parameters?.docs?.source}}},ot.parameters={...ot.parameters,docs:{...ot.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - iPhone recovery retry failure stays verify-only and actionable',
  args: {
    data: fixtures,
    apiStatus: {
      ...tokenRequiredStatus,
      baseUrl: 'storybook://device-enrollment-recovery-failure'
    },
    initialRoute: 'devices',
    addIPhoneServices: enrollmentRecoveryFailureServices,
    iPhoneSoundPlayer: iPhoneSoundStory
  },
  play: async ({
    canvasElement
  }) => {
    startEnrollmentRecoveryStory.mockClear();
    retryEnrollmentRecoveryFailureStory.mockClear();
    iPhoneSoundStory.mockClear();
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    await userEvent.click(canvas.getByRole('button', {
      name: 'Add to Sideport'
    }));
    await userEvent.click(within(page.getByRole('group', {
      name: 'Add to Sideport choices'
    })).getByRole('button', {
      name: /Add iPhone/
    }));
    const dialog = page.getByRole('dialog', {
      name: 'Add an iPhone'
    });
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Connect iPhone'
    }));
    await waitFor(() => expect(retryEnrollmentRecoveryFailureStory).toHaveBeenCalledWith(enrollmentRecoverySource.operationId));
    await expect(within(dialog).getByRole('alert')).toHaveTextContent('Sideport briefly lost the secure iPhone connection.');
    await expect(within(dialog).getByRole('button', {
      name: 'Try checking again'
    })).toBeVisible();
    await expect(within(dialog).queryByRole('button', {
      name: /pair/i
    })).not.toBeInTheDocument();
    await expect(within(dialog).queryByRole('button', {
      name: /trust/i
    })).not.toBeInTheDocument();
    await expect(startEnrollmentRecoveryStory).toHaveBeenCalledTimes(1);
  }
}`,...ot.parameters?.docs?.source}}},Q.parameters={...Q.parameters,docs:{...Q.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - command menu includes Add actions',
  args: {
    data: fixtures,
    apiStatus: completedOnboardingStatus,
    initialRoute: 'settings',
    initialCommandOpen: true
  },
  play: async ({
    canvasElement
  }) => {
    const page = within(canvasElement.ownerDocument.body);
    const commandDialog = page.getByRole('dialog', {
      name: 'Search and commands'
    });
    const addIPhone = within(commandDialog).getByRole('button', {
      name: /Add iPhone/
    });
    const addApp = within(commandDialog).getByRole('button', {
      name: /Add app/
    });
    await waitFor(() => expect(addIPhone).toBeVisible());
    await waitFor(() => expect(addApp).toBeVisible());
    await userEvent.click(addIPhone);
    await expect(page.getByRole('dialog', {
      name: 'Add an iPhone'
    })).toBeVisible();
  }
}`,...Q.parameters?.docs?.source}}},$.parameters={...$.parameters,docs:{...$.parameters?.docs,source:{originalSource:`{
  name: 'Signed in - Member can add iPhone without Owner controls',
  args: {
    data: memberData,
    apiStatus: completedOnboardingStatus,
    initialRoute: 'settings'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    await expect(canvas.queryByRole('heading', {
      name: 'Connect Apple data without over-trusting it'
    })).not.toBeInTheDocument();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Add to Sideport'
    }));
    const addChoices = page.getByRole('group', {
      name: 'Add to Sideport choices'
    });
    await expect(within(addChoices).getByRole('button', {
      name: /Add iPhone/
    })).toBeVisible();
    await expect(within(addChoices).queryByRole('button', {
      name: /Add app/
    })).not.toBeInTheDocument();
  }
}`,...$.parameters?.docs?.source}}},st=`OverviewHealthy.FirstRunOnboarding.OwnerAccountCanEnterPortalWithoutIPhone.FirstRunConnectIPhoneActionable.FirstRunConnectIPhoneResumesWithoutStartingAgain.LiveOnboarding.OnboardingInstallStartsInline.OnboardingInstallRequestError.FirstRunOnboardingMobile390.CompletedOnboardingReload.OnboardingFinalizationRecovery.OnboardingUnknownInstallReconciliation.OnboardingTerminalLineageBlock.DeviceInventory.DeviceDetailTwoApps.AppCatalogSeed.AppCatalogSearch.InstallAppOneAction.InstallAppReloadResume.PendingRegistrationState.ActivityOperations.PeopleWorkspace.SettingsConsolidated.SettingsSchedulerPolicy.EmptyFleet.AnisetteBlocked.ApiUnavailableRuntime.TokenRequiredSettings.OidcAppleRequestSecurity.CommandMenuOpen.DeviceDetailTabbed.GlobalAddMenu.AddIPhoneAutomatic.AddAppSources.GlobalAddMobile390.AddAppRuntimeBound.AddAppSourcesLoadIndependently.AddIPhoneRuntimeBound.AddIPhoneResumeAfterClose.AddIPhoneRecoveryRetry.AddIPhoneRecoveryRetryFailure.CommandMenuAddActions.MemberCapabilityBoundaries`.split(`.`)}))();export{G as ActivityOperations,tt as AddAppRuntimeBound,$e as AddAppSources,nt as AddAppSourcesLoadIndependently,Qe as AddIPhoneAutomatic,at as AddIPhoneRecoveryRetry,ot as AddIPhoneRecoveryRetryFailure,it as AddIPhoneResumeAfterClose,rt as AddIPhoneRuntimeBound,X as AnisetteBlocked,qe as ApiUnavailableRuntime,V as AppCatalogSearch,B as AppCatalogSeed,Q as CommandMenuAddActions,Xe as CommandMenuOpen,F as CompletedOnboardingReload,Z as DeviceDetailTabbed,z as DeviceDetailTwoApps,Ke as DeviceInventory,Y as EmptyFleet,k as FirstRunConnectIPhoneActionable,A as FirstRunConnectIPhoneResumesWithoutStartingAgain,D as FirstRunOnboarding,P as FirstRunOnboardingMobile390,Ze as GlobalAddMenu,et as GlobalAddMobile390,H as InstallAppOneAction,U as InstallAppReloadResume,j as LiveOnboarding,$ as MemberCapabilityBoundaries,Ye as OidcAppleRequestSecurity,I as OnboardingFinalizationRecovery,N as OnboardingInstallRequestError,M as OnboardingInstallStartsInline,R as OnboardingTerminalLineageBlock,L as OnboardingUnknownInstallReconciliation,E as OverviewHealthy,O as OwnerAccountCanEnterPortalWithoutIPhone,W as PendingRegistrationState,K as PeopleWorkspace,q as SettingsConsolidated,J as SettingsSchedulerPolicy,Je as TokenRequiredSettings,st as __namedExportsOrder,ae as default};