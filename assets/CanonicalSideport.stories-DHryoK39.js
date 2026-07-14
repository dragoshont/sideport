import{i as e,s as t}from"./preload-helper-xPQekRTU.js";import{c as n,l as r}from"./iframe-Q-TNvOPE.js";import{At as i,E as a,Et as o,H as s,It as c,M as l,Mt as u,Ot as d,P as f,Pt as p,R as m,S as h,St as g,X as _,b as ee,c as te,ct as ne,et as v,gt as re,i as ie,mt as ae,n as y,nt as oe,o as b,ot as se,t as ce,w as x}from"./lucide-react-DqF6b3Ks.js";import{t as le}from"./CanonicalSideport-zq54A334.js";function ue({app:e,compact:t=!1}){return(0,O.jsx)(`span`,{"aria-hidden":`true`,className:`spc-app-icon ${e.tone} ${t?`compact`:``}`,children:e.initials})}function S(){return(0,O.jsxs)(`span`,{className:`spc-proposed-badge`,children:[(0,O.jsx)(ee,{"aria-hidden":`true`,size:13}),` Proposed experience`]})}function C(){return(0,O.jsxs)(`div`,{className:`spc-simulation-note`,role:`note`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:16}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`Storybook simulation`}),(0,O.jsx)(`small`,{children:`No invitation, sign-in, account, device, app, or audio action occurs.`})]})]})}function w(){return(0,O.jsxs)(`div`,{className:`spc-brand`,children:[(0,O.jsx)(`span`,{className:`spc-brand-mark`,children:(0,O.jsx)(s,{"aria-hidden":`true`,size:19})}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`Sideport`}),(0,O.jsx)(`small`,{children:`Apps for people you trust`})]})]})}function T({eyebrow:e,title:t,children:n,action:r}){return(0,O.jsxs)(`header`,{className:`spc-page-heading`,children:[(0,O.jsxs)(`div`,{children:[e?(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:e}):null,(0,O.jsx)(`h1`,{tabIndex:-1,children:t}),(0,O.jsx)(`p`,{children:n})]}),r?(0,O.jsx)(`div`,{className:`spc-page-action`,children:r}):null]})}function E({children:e,tone:t=`positive`}){return(0,O.jsxs)(`span`,{className:`spc-status-pill ${t}`,children:[(0,O.jsx)(`span`,{"aria-hidden":`true`,className:`spc-status-dot`}),e]})}function de({role:e,onAddIPhone:t,onNavigate:n}){return(0,O.jsxs)(`div`,{className:`spc-page`,"data-page":`home`,children:[(0,O.jsx)(T,{eyebrow:e===`owner`?`Your Sideport`:`Welcome home`,title:e===`owner`?`Apps and iPhones at a glance`:`Your apps are ready`,children:e===`owner`?`Sideport keeps watching for connected iPhones and handles approved app updates in the background.`:`Sideport will attempt refreshes over paired home Wi-Fi and use the cable as the reliable fallback.`}),(0,O.jsxs)(`section`,{className:`spc-focus-card`,"aria-labelledby":`home-device-title`,children:[(0,O.jsx)(`div`,{className:`spc-device-visual`,children:(0,O.jsx)(u,{"aria-hidden":`true`,size:34})}),(0,O.jsxs)(`div`,{className:`spc-focus-copy`,children:[(0,O.jsx)(E,{children:`Watching`}),(0,O.jsx)(`h2`,{id:`home-device-title`,children:e===`owner`?`Cable ready for any trusted iPhone`:`Your iPhone is connected`}),(0,O.jsx)(`p`,{children:e===`owner`?`Leave the cable attached to the Sideport host. A trusted iPhone is detected automatically when someone plugs it in.`:`Home Wi-Fi available · cable ready when an update needs it`})]}),(0,O.jsx)(`button`,{className:`spc-button secondary`,onClick:()=>n(`devices`),type:`button`,children:`View devices`})]}),(0,O.jsxs)(`div`,{className:`spc-two-column`,children:[(0,O.jsxs)(`section`,{className:`spc-section`,"aria-labelledby":`home-apps-title`,children:[(0,O.jsxs)(`div`,{className:`spc-section-heading`,children:[(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Apps`}),(0,O.jsx)(`h2`,{id:`home-apps-title`,children:`1 update available`})]}),(0,O.jsxs)(`button`,{className:`spc-text-button`,onClick:()=>n(`apps`),type:`button`,children:[`Find apps `,(0,O.jsx)(d,{"aria-hidden":`true`,size:16})]})]}),(0,O.jsxs)(`div`,{className:`spc-list-row`,children:[(0,O.jsx)(ue,{app:k[1],compact:!0}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Dice Roll 0.1.1`}),(0,O.jsx)(`small`,{children:`Available for Mara and Alex`})]}),(0,O.jsx)(E,{tone:`warning`,children:`Update`})]})]}),(0,O.jsxs)(`section`,{className:`spc-section`,"aria-labelledby":`home-next-title`,children:[(0,O.jsx)(`div`,{className:`spc-section-heading`,children:(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Devices`}),(0,O.jsx)(`h2`,{id:`home-next-title`,children:`3 people · 3 iPhones`})]})}),(0,O.jsx)(`p`,{className:`spc-section-copy`,children:`Mara is home on Wi-Fi. Alex is away. Sam’s iPhone needs the cable for one update.`}),e===`owner`?(0,O.jsxs)(`button`,{className:`spc-button secondary full`,onClick:e=>t(e.currentTarget),type:`button`,children:[(0,O.jsx)(m,{"aria-hidden":`true`,size:17}),` Add another iPhone`]}):(0,O.jsx)(`p`,{className:`spc-section-copy`,children:`Need another iPhone? Ask the Sideport owner to add it after checking Apple’s device limit.`})]})]})]})}function fe({onClose:e}){let[t,n]=(0,D.useState)(`upload`);return(0,O.jsxs)(`section`,{className:`spc-import-panel`,"aria-labelledby":`import-app-title`,children:[(0,O.jsxs)(`div`,{className:`spc-section-heading`,children:[(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Import app`}),(0,O.jsx)(`h2`,{id:`import-app-title`,children:`Where is the IPA?`})]}),(0,O.jsx)(`button`,{"aria-label":`Close app import`,className:`spc-icon-button`,onClick:e,type:`button`,children:(0,O.jsx)(y,{"aria-hidden":`true`,size:19})})]}),(0,O.jsxs)(`div`,{"aria-label":`IPA source`,className:`spc-source-choices`,role:`group`,children:[(0,O.jsxs)(`button`,{"aria-pressed":t===`upload`,onClick:()=>n(`upload`),type:`button`,children:[(0,O.jsx)(ae,{"aria-hidden":`true`,size:21}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`This computer`}),(0,O.jsx)(`small`,{children:`Choose an IPA file`})]})]}),(0,O.jsxs)(`button`,{"aria-pressed":t===`storage`,onClick:()=>n(`storage`),type:`button`,children:[(0,O.jsx)(se,{"aria-hidden":`true`,size:21}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`On this Sideport`}),(0,O.jsx)(`small`,{children:`Browse managed server storage`})]})]}),(0,O.jsxs)(`button`,{"aria-pressed":t===`github`,onClick:()=>n(`github`),type:`button`,children:[(0,O.jsx)(ne,{"aria-hidden":`true`,size:21}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`GitHub release`}),(0,O.jsx)(`small`,{children:`Public or selected private repository`})]})]})]}),t===`upload`?(0,O.jsxs)(`div`,{className:`spc-source-detail`,children:[(0,O.jsx)(`strong`,{children:`Choose an IPA from this computer`}),(0,O.jsx)(`span`,{children:`The runtime will inspect its name, icon, bundle, version, and size before it becomes available.`}),(0,O.jsx)(`button`,{className:`spc-button secondary`,disabled:!0,type:`button`,children:`Choose IPA in runtime`})]}):null,t===`storage`?(0,O.jsxs)(`div`,{className:`spc-source-detail`,children:[(0,O.jsx)(`strong`,{children:`Browse Sideport storage`}),(0,O.jsx)(`span`,{children:`The runtime will list managed IPA files without asking a member for a server path.`}),(0,O.jsx)(`div`,{className:`spc-mini-apps`,children:k.map(e=>(0,O.jsxs)(`span`,{children:[(0,O.jsx)(ue,{app:e,compact:!0}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:e.name}),(0,O.jsxs)(`small`,{children:[`Version `,e.version]})]})]},e.id))})]}):null,t===`github`?(0,O.jsxs)(`div`,{className:`spc-source-detail`,children:[(0,O.jsx)(`strong`,{children:`Connect one selected repository`}),(0,O.jsx)(`span`,{children:`Private access is requested only for the repository the owner selects.`}),(0,O.jsxs)(`dl`,{className:`spc-permission-list`,children:[(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`dt`,{children:`Metadata`}),(0,O.jsx)(`dd`,{children:`Read`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`dt`,{children:`Contents`}),(0,O.jsx)(`dd`,{children:`Read`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`dt`,{children:`Write access`}),(0,O.jsx)(`dd`,{children:`None`})]})]}),(0,O.jsx)(`button`,{className:`spc-button secondary`,disabled:!0,type:`button`,children:`Connect GitHub in runtime`})]}):null]})}function pe({importOpen:e,memberName:t,onInstall:n,onToggleImport:r,role:i}){let[a,o]=(0,D.useState)(``),[s,c]=(0,D.useState)(null),u=(0,D.useRef)(null),d=(0,D.useRef)(null),f=k.filter(e=>`${e.name} ${e.description}`.toLowerCase().includes(a.toLowerCase())),p=k.find(e=>e.id===s);return(0,D.useEffect)(()=>{s&&u.current?.focus()},[s]),(0,O.jsxs)(`div`,{className:`spc-page`,"data-page":`apps`,children:[(0,O.jsx)(T,{eyebrow:`Approved by the owner`,title:`Apps`,action:i===`owner`?(0,O.jsxs)(`button`,{"aria-expanded":e,className:`spc-button secondary`,onClick:r,type:`button`,children:[(0,O.jsx)(ae,{"aria-hidden":`true`,size:17}),` Import app`]}):void 0,children:`Find an app, choose an iPhone, and install. Sideport keeps approved apps updated afterward.`}),i===`owner`&&e?(0,O.jsx)(fe,{onClose:r}):null,(0,O.jsxs)(`label`,{className:`spc-page-search`,children:[(0,O.jsx)(l,{"aria-hidden":`true`,size:18}),(0,O.jsx)(`span`,{className:`spc-visually-hidden`,children:`Search apps`}),(0,O.jsx)(`input`,{onChange:e=>o(e.currentTarget.value),placeholder:`Search approved apps`,type:`search`,value:a})]}),(0,O.jsxs)(`div`,{className:`spc-filter-chips`,"aria-label":`App filters`,role:`group`,children:[(0,O.jsx)(`button`,{"aria-pressed":`true`,type:`button`,children:`All apps`}),(0,O.jsx)(`button`,{"aria-pressed":`false`,type:`button`,children:`Updates`}),(0,O.jsx)(`button`,{"aria-pressed":`false`,type:`button`,children:`Installed`})]}),(0,O.jsx)(`div`,{className:`spc-app-grid`,children:f.map(e=>(0,O.jsxs)(`article`,{className:`spc-app-card`,children:[(0,O.jsxs)(`div`,{className:`spc-app-card-top`,children:[(0,O.jsx)(ue,{app:e}),(0,O.jsx)(E,{tone:e.release===`Update available`?`warning`:`quiet`,children:e.release})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:e.name}),(0,O.jsx)(`p`,{children:e.description})]}),(0,O.jsxs)(`div`,{className:`spc-app-meta`,children:[(0,O.jsxs)(`span`,{children:[`Version `,e.version,` · `,e.source]}),(0,O.jsx)(`span`,{children:e.installedOn})]}),(0,O.jsx)(`button`,{className:`spc-button primary full`,onClick:r=>{i===`owner`?(d.current=r.currentTarget,c(e.id)):n(e.id,t)},type:`button`,children:i===`owner`?`Choose iPhone`:`Install`})]},e.id))}),i===`owner`&&p?(0,O.jsxs)(`section`,{className:`spc-target-picker`,"aria-labelledby":`target-picker-title`,children:[(0,O.jsxs)(`div`,{children:[(0,O.jsxs)(`span`,{className:`spc-eyebrow`,children:[`Install `,p.name]}),(0,O.jsx)(`h2`,{id:`target-picker-title`,ref:u,tabIndex:-1,children:`Choose the iPhone by name`}),(0,O.jsx)(`p`,{children:`Sideport will wait for that exact iPhone on the cable before installation starts.`})]}),(0,O.jsxs)(`div`,{role:`group`,"aria-label":`Target iPhone`,children:[(0,O.jsxs)(`button`,{className:`spc-button secondary`,onClick:()=>n(p.id,`Mara`),type:`button`,children:[(0,O.jsx)(h,{"aria-hidden":`true`,size:17}),` Mara’s iPhone`]}),(0,O.jsxs)(`button`,{className:`spc-button secondary`,onClick:()=>n(p.id,`Alex`),type:`button`,children:[(0,O.jsx)(h,{"aria-hidden":`true`,size:17}),` Alex’s iPhone`]}),(0,O.jsx)(`button`,{"aria-label":`Cancel iPhone choice`,className:`spc-icon-button`,onClick:()=>{c(null),d.current?.focus()},type:`button`,children:(0,O.jsx)(y,{"aria-hidden":`true`,size:18})})]})]}):null,i===`owner`?(0,O.jsxs)(`aside`,{className:`spc-inline-note`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Need another app?`}),(0,O.jsx)(`span`,{children:`Import an IPA from this computer, Sideport storage, or an approved public or private GitHub repository.`})]})]}):null]})}function me({role:e,onAddIPhone:t}){return(0,O.jsxs)(`div`,{className:`spc-page`,"data-page":`devices`,children:[(0,O.jsx)(T,{eyebrow:`Always watching the Sideport cable`,title:`Devices`,action:e===`owner`?(0,O.jsxs)(`button`,{className:`spc-button primary`,onClick:e=>t(e.currentTarget),type:`button`,children:[(0,O.jsx)(m,{"aria-hidden":`true`,size:17}),` Add iPhone`]}):void 0,children:e===`owner`?`See who owns each iPhone, where Sideport can reach it, and whether an app needs attention.`:`Your iPhone and its approved Sideport apps.`}),(0,O.jsxs)(`aside`,{"aria-label":`USB port monitoring status`,className:`spc-port-monitor`,"aria-live":`polite`,children:[(0,O.jsx)(`span`,{className:`spc-pulse-dot`}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`USB port monitor is active`}),(0,O.jsx)(`span`,{children:`Plug in an already trusted iPhone and Sideport will recognize it automatically. A new iPhone starts the guided Trust setup.`})]})]}),(0,O.jsxs)(`section`,{className:`spc-section spc-device-list`,"aria-label":`iPhones`,children:[(0,O.jsxs)(`article`,{className:`spc-device-row`,children:[(0,O.jsx)(`span`,{className:`spc-round-icon`,children:(0,O.jsx)(h,{"aria-hidden":`true`,size:23})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:e===`owner`?`Mara’s iPhone`:`Your iPhone`}),(0,O.jsxs)(`p`,{children:[e===`owner`?`Mara · Member`:`iPhone 15`,` · 3 apps installed`]})]}),(0,O.jsxs)(`div`,{className:`spc-device-status`,children:[(0,O.jsx)(E,{children:`Home Wi-Fi`}),(0,O.jsx)(`small`,{children:`Up to date · seen now`})]}),(0,O.jsx)(`button`,{"aria-label":`Open iPhone details`,className:`spc-icon-button`,type:`button`,children:(0,O.jsx)(d,{"aria-hidden":`true`,size:20})})]}),e===`owner`?(0,O.jsxs)(`article`,{className:`spc-device-row`,children:[(0,O.jsx)(`span`,{className:`spc-round-icon`,children:(0,O.jsx)(h,{"aria-hidden":`true`,size:23})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:`Alex’s iPhone`}),(0,O.jsx)(`p`,{children:`Alex · Member · 2 apps installed`})]}),(0,O.jsxs)(`div`,{className:`spc-device-status`,children:[(0,O.jsx)(E,{tone:`quiet`,children:`Away`}),(0,O.jsx)(`small`,{children:`Up to date · seen yesterday`})]}),(0,O.jsx)(`button`,{"aria-label":`Open Alex’s iPhone details`,className:`spc-icon-button`,type:`button`,children:(0,O.jsx)(d,{"aria-hidden":`true`,size:20})})]}):null,e===`owner`?(0,O.jsxs)(`article`,{className:`spc-device-row`,children:[(0,O.jsx)(`span`,{className:`spc-round-icon`,children:(0,O.jsx)(h,{"aria-hidden":`true`,size:23})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:`Sam’s iPhone`}),(0,O.jsx)(`p`,{children:`Sam · Member · 2 apps installed`})]}),(0,O.jsxs)(`div`,{className:`spc-device-status`,children:[(0,O.jsx)(E,{tone:`warning`,children:`Cable needed`}),(0,O.jsx)(`small`,{children:`Dice Roll update waiting`})]}),(0,O.jsx)(`button`,{"aria-label":`Open Sam’s iPhone details`,className:`spc-icon-button`,type:`button`,children:(0,O.jsx)(d,{"aria-hidden":`true`,size:20})})]}):null]}),e===`member`?(0,O.jsxs)(`aside`,{className:`spc-inline-note`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Need another iPhone?`}),(0,O.jsx)(`span`,{children:`Ask the Sideport owner. They will check available Apple device capacity before connecting it.`})]})]}):null,(0,O.jsxs)(`aside`,{className:`spc-inline-note`,children:[(0,O.jsx)(u,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`The cable is only required for setup and reliable fallback.`}),(0,O.jsx)(`span`,{children:`Sideport tries home Wi-Fi first. If a refresh cannot finish, reconnect this iPhone to the Sideport cable.`})]})]})]})}function he({role:e}){let[t,n]=(0,D.useState)(``),[r,i]=(0,D.useState)(!1);return(0,O.jsxs)(`div`,{className:`spc-page`,"data-page":`people`,children:[(0,O.jsx)(T,{eyebrow:`Your Sideport`,title:`People`,children:e===`owner`?`Invite someone you trust. They sign in with a passkey and can use only approved apps on their own iPhone.`:`People who share this Sideport with you.`}),e===`owner`?(0,O.jsxs)(`section`,{className:`spc-invite-card`,"aria-labelledby":`invite-title`,children:[(0,O.jsx)(`span`,{className:`spc-round-icon accent`,children:(0,O.jsx)(te,{"aria-hidden":`true`,size:23})}),(0,O.jsxs)(`div`,{className:`spc-invite-copy`,children:[(0,O.jsx)(`h2`,{id:`invite-title`,children:`Invite someone you trust`}),(0,O.jsx)(`p`,{children:`We’ll create a private, single-use link. They will use Face ID, Touch ID, Windows Hello, or their password manager—never your Apple signing password.`})]}),r?(0,O.jsxs)(`div`,{className:`spc-invite-result`,role:`status`,children:[(0,O.jsx)(o,{"aria-hidden":`true`,size:20}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Invitation ready`}),(0,O.jsxs)(`span`,{children:[`Copy the link and send it to `,t,`.`]})]}),(0,O.jsx)(`button`,{className:`spc-button secondary`,type:`button`,children:`Copy link`})]}):(0,O.jsxs)(`form`,{className:`spc-invite-form`,onSubmit:e=>{e.preventDefault(),t.trim()&&i(!0)},children:[(0,O.jsx)(`label`,{htmlFor:`member-email`,children:`Their email`}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`input`,{id:`member-email`,onChange:e=>n(e.currentTarget.value),placeholder:`name@example.com`,type:`email`,value:t}),(0,O.jsx)(`button`,{className:`spc-button primary`,disabled:!t.trim(),type:`submit`,children:`Create invitation`})]}),(0,O.jsx)(`small`,{children:`Access: Member · app sources and Apple signing stay owner-only.`})]})]}):(0,O.jsxs)(`aside`,{className:`spc-inline-note`,children:[(0,O.jsx)(x,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`You have Member access.`}),(0,O.jsx)(`span`,{children:`You can use your iPhone and install approved apps. Another iPhone requires the owner to check capacity first.`})]})]}),(0,O.jsxs)(`section`,{className:`spc-section`,"aria-labelledby":`people-list-title`,children:[(0,O.jsx)(`div`,{className:`spc-section-heading`,children:(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Members`}),(0,O.jsx)(`h2`,{id:`people-list-title`,children:`3 people`})]})}),[[`Dragos`,`Owner · signing and member access`],[`Mara`,e===`member`?`You · Member · 1 iPhone`:`Member · 1 iPhone`],[`Alex`,`Member · 1 iPhone`]].map(([e,t])=>(0,O.jsxs)(`div`,{className:`spc-list-row`,children:[(0,O.jsx)(`span`,{className:`spc-avatar`,children:e[0]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:e}),(0,O.jsx)(`small`,{children:t})]}),(0,O.jsx)(E,{tone:`quiet`,children:`Active`})]},e))]})]})}function ge({role:e}){let[t,n]=(0,D.useState)(!1),[r,a]=(0,D.useState)(`all`);return(0,O.jsxs)(`div`,{className:`spc-page`,"data-page":`activity`,children:[(0,O.jsx)(T,{eyebrow:`What happened and who needs help`,title:`Activity`,action:e===`owner`?(0,O.jsxs)(`button`,{"aria-expanded":t,className:`spc-button secondary`,onClick:()=>n(e=>!e),type:`button`,children:[t?`Hide`:`Show`,` technical details`]}):void 0,children:e===`owner`?`Updates across people, iPhones, apps, and access—in one chronological feed.`:`Installs and updates for your iPhone, in plain language.`}),(0,O.jsx)(`div`,{className:`spc-filter-chips`,"aria-label":`Activity filters`,role:`group`,children:[[`all`,`All`],[`attention`,`Needs attention`],[`apps`,`Apps`],[`devices`,`Devices`]].map(([e,t])=>(0,O.jsx)(`button`,{"aria-pressed":r===e,onClick:()=>a(e),type:`button`,children:t},e))}),(0,O.jsxs)(`section`,{className:`spc-section spc-timeline`,"aria-label":`Recent activity`,children:[r===`all`||r===`attention`||r===`devices`?(0,O.jsxs)(O.Fragment,{children:[(0,O.jsx)(`h2`,{className:`spc-timeline-group`,children:`Needs attention`}),(0,O.jsxs)(`article`,{className:`attention`,children:[(0,O.jsx)(`span`,{className:`spc-timeline-icon warning`,children:(0,O.jsx)(u,{"aria-hidden":`true`,size:17})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:`Sam’s iPhone needs the cable`}),(0,O.jsx)(`p`,{children:`Dice Roll could not update over Wi-Fi. Plug it into the Sideport host; the update starts automatically.`}),e===`owner`&&t?(0,O.jsx)(`code`,{children:`refresh waiting-for-usb · member sam · retry preserved`}):null,(0,O.jsxs)(`button`,{className:`spc-text-button`,type:`button`,children:[`View device `,(0,O.jsx)(d,{"aria-hidden":`true`,size:15})]})]}),(0,O.jsx)(`time`,{children:`10 min ago`})]})]}):null,r===`attention`?null:(0,O.jsx)(`h2`,{className:`spc-timeline-group`,children:`Today`}),r===`all`||r===`apps`?(0,O.jsxs)(`article`,{children:[(0,O.jsx)(`span`,{className:`spc-timeline-icon success`,children:(0,O.jsx)(i,{"aria-hidden":`true`,size:17})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:`Cert Clock updated on Mara’s iPhone`}),(0,O.jsx)(`p`,{children:`Version 0.1.1 installed and verified.`}),e===`owner`&&t?(0,O.jsx)(`code`,{children:`operation op_refresh_01 · member mara · device evidence verified`}):null]}),(0,O.jsx)(`time`,{children:`09:42`})]}):null,r===`all`||r===`devices`?(0,O.jsxs)(`article`,{children:[(0,O.jsx)(`span`,{className:`spc-timeline-icon`,children:(0,O.jsx)(h,{"aria-hidden":`true`,size:17})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:e===`owner`?`Alex’s iPhone came home`:`Your iPhone came home`}),(0,O.jsx)(`p`,{children:`Sideport can reach it over paired Wi-Fi. No update is due.`}),e===`owner`&&t?(0,O.jsx)(`code`,{children:`transport network-usbmux · lockdown trusted`}):null]}),(0,O.jsx)(`time`,{children:`08:15`})]}):null,r===`all`||r===`apps`?(0,O.jsxs)(`article`,{children:[(0,O.jsx)(`span`,{className:`spc-timeline-icon`,children:(0,O.jsx)(re,{"aria-hidden":`true`,size:17})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:`Dice Roll 0.1.1 became available`}),(0,O.jsx)(`p`,{children:`Imported from the approved GitHub release. Installed devices can update automatically.`}),e===`owner`&&t?(0,O.jsx)(`code`,{children:`catalog source github · artifact inspected · owner action`}):null]}),(0,O.jsx)(`time`,{children:`07:30`})]}):null,e===`owner`&&r===`all`?(0,O.jsxs)(O.Fragment,{children:[(0,O.jsx)(`h2`,{className:`spc-timeline-group`,children:`Earlier`}),(0,O.jsxs)(`article`,{children:[(0,O.jsx)(`span`,{className:`spc-timeline-icon`,children:(0,O.jsx)(g,{"aria-hidden":`true`,size:17})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:`Sam joined Sideport`}),(0,O.jsx)(`p`,{children:`Member access activated · Sam’s iPhone was added and trusted.`}),t?(0,O.jsx)(`code`,{children:`membership accepted · resource owner member_sam`}):null]}),(0,O.jsx)(`time`,{children:`Monday`})]})]}):null]})]})}function _e({role:e}){let[t,n]=(0,D.useState)(!1),[r,a]=(0,D.useState)(!1),[o,s]=(0,D.useState)(`summary`),[c,l]=(0,D.useState)(`storybook-demo`),[u,m]=(0,D.useState)(!1),h=(0,D.useRef)(null);(0,D.useEffect)(()=>{r&&h.current?.focus()},[r,o]),(0,D.useEffect)(()=>{if(o!==`working`)return;let e=window.setTimeout(()=>s(`done`),800);return()=>window.clearTimeout(e)},[o]);let g=()=>{a(!1),s(`summary`),l(`storybook-demo`),m(!1)};return(0,O.jsxs)(`div`,{className:`spc-page`,"data-page":`settings`,children:[(0,O.jsx)(T,{eyebrow:`Simple by default`,title:`Settings`,children:`Your account, automatic refresh, and recovery.`}),(0,O.jsxs)(`div`,{className:`spc-settings-list`,children:[(0,O.jsxs)(`section`,{className:`spc-setting-row`,children:[(0,O.jsx)(`span`,{className:`spc-round-icon`,children:(0,O.jsx)(x,{"aria-hidden":`true`,size:22})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:`Sign-in and recovery`}),(0,O.jsx)(`p`,{children:`Managed by Authentik · passkeys stay on your trusted devices.`})]}),(0,O.jsx)(`button`,{className:`spc-button secondary`,type:`button`,children:`Open Authentik`})]}),(0,O.jsxs)(`section`,{className:`spc-setting-row`,children:[(0,O.jsx)(`span`,{className:`spc-round-icon`,children:(0,O.jsx)(f,{"aria-hidden":`true`,size:22})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:`Automatic refresh`}),(0,O.jsx)(`p`,{children:`On · paired Wi-Fi is attempted; the Sideport cable is the reliable fallback.`})]}),(0,O.jsx)(E,{children:`On`})]}),e===`owner`?(0,O.jsxs)(`section`,{className:`spc-setting-row`,children:[(0,O.jsx)(`span`,{className:`spc-round-icon`,children:(0,O.jsx)(p,{"aria-hidden":`true`,size:22})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:`Signing`}),(0,O.jsx)(`p`,{children:`d•••••@icloud.com · Personal Team returned by Apple`})]}),(0,O.jsx)(`button`,{"aria-label":`Review Apple signing`,className:`spc-button secondary`,onClick:()=>a(!0),type:`button`,children:`Review`})]}):null,e===`owner`?(0,O.jsxs)(`section`,{className:`spc-setting-row`,children:[(0,O.jsx)(`span`,{className:`spc-round-icon`,children:(0,O.jsx)(_,{"aria-hidden":`true`,size:22})}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`h2`,{children:`Sideport setup`}),(0,O.jsx)(`p`,{children:`Completed · current services are ready.`})]}),(0,O.jsx)(`button`,{className:`spc-button secondary`,type:`button`,children:`Review`})]}):null]}),e===`owner`?(0,O.jsxs)(`button`,{"aria-expanded":t,className:`spc-disclosure`,onClick:()=>n(e=>!e),type:`button`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:17}),` `,t?`Hide technical details`:`Show technical details`,` `,(0,O.jsx)(d,{"aria-hidden":`true`,size:17})]}):null,e===`owner`&&t?(0,O.jsxs)(`section`,{className:`spc-technical-panel`,children:[(0,O.jsx)(`h2`,{children:`Technical details`}),(0,O.jsxs)(`dl`,{children:[(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`dt`,{children:`Deployment`}),(0,O.jsx)(`dd`,{children:`Docker · healthy`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`dt`,{children:`Device link`}),(0,O.jsx)(`dd`,{children:`usbmuxd · available`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`dt`,{children:`Signer`}),(0,O.jsx)(`dd`,{children:`Ready · credential redacted`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`dt`,{children:`Observability`}),(0,O.jsx)(`dd`,{children:`Local activity only`})]})]})]}):null,e===`owner`&&r?(0,O.jsx)(`div`,{"aria-label":`Review Apple signing`,"aria-modal":`true`,className:`spc-dialog-backdrop`,role:`dialog`,children:(0,O.jsxs)(`section`,{className:`spc-signing-dialog`,children:[(0,O.jsx)(`button`,{"aria-label":`Close signing review`,className:`spc-icon-button spc-dialog-close`,onClick:g,type:`button`,children:(0,O.jsx)(y,{"aria-hidden":`true`,size:20})}),o===`summary`?(0,O.jsxs)(O.Fragment,{children:[(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Owner-only signing`}),(0,O.jsx)(`h1`,{ref:h,tabIndex:-1,children:`Apple signing`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Sideport uses one Apple account and one team for every approved app. Members never see or change this access.`}),(0,O.jsxs)(`div`,{className:`spc-signing-summary`,children:[(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{children:`Apple account`}),(0,O.jsx)(`strong`,{children:`d•••••@icloud.com`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{children:`Apple Developer Team`}),(0,O.jsx)(`strong`,{children:`Dragos Personal Team`}),(0,O.jsx)(`small`,{children:`Returned by Apple · selected automatically`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{children:`Signing identity`}),(0,O.jsx)(`strong`,{children:`Ready until June 30, 2027`}),(0,O.jsx)(`small`,{children:`Certificate ending A1B2`})]})]}),(0,O.jsx)(`button`,{className:`spc-button secondary large`,onClick:()=>s(`reauth`),type:`button`,children:`Change account or team`}),(0,O.jsx)(`p`,{className:`spc-fine-print`,children:`Nothing changes until Sideport shows the exact impact and you confirm it.`})]}):null,o===`reauth`?(0,O.jsxs)(`form`,{onSubmit:e=>{e.preventDefault(),l(``),s(`impact`)},children:[(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Fresh Apple sign-in required`}),(0,O.jsx)(`h1`,{ref:h,tabIndex:-1,children:`Confirm your Apple account`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Sign in again so Sideport can fetch the current teams and certificate inventory. This does not change signing yet.`}),(0,O.jsx)(`label`,{htmlFor:`signing-account`,children:`Apple Account`}),(0,O.jsx)(`input`,{autoComplete:`username`,id:`signing-account`,readOnly:!0,type:`email`,value:`dragos@icloud.com`}),(0,O.jsx)(`label`,{htmlFor:`signing-password`,children:`Password`}),(0,O.jsx)(`input`,{autoComplete:`current-password`,id:`signing-password`,onChange:e=>l(e.currentTarget.value),type:`password`,value:c}),(0,O.jsx)(`button`,{className:`spc-button primary large`,disabled:!c,type:`submit`,children:`Check current signing`}),(0,O.jsx)(`button`,{className:`spc-text-button centered-button`,onClick:()=>s(`summary`),type:`button`,children:`Back`})]}):null,o===`impact`?(0,O.jsxs)(O.Fragment,{children:[(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Review before changing`}),(0,O.jsx)(`h1`,{ref:h,tabIndex:-1,children:`Replace the current signing identity?`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Apple already has one development certificate that Sideport cannot reuse. Replacing it may affect apps signed elsewhere.`}),(0,O.jsxs)(`section`,{className:`spc-impact-card`,"aria-label":`Exact signing impact`,children:[(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`1 certificate`}),(0,O.jsx)(`span`,{children:`Certificate ending A1B2 will be revoked`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`3 Sideport apps`}),(0,O.jsx)(`span`,{children:`They remain installed and will use the new identity on their next refresh`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`3 iPhones · 3 profiles`}),(0,O.jsx)(`span`,{children:`Registrations remain; profiles are recreated only when each app refreshes`})]})]}),(0,O.jsxs)(`label`,{className:`spc-confirm-row`,children:[(0,O.jsx)(`input`,{checked:u,onChange:e=>m(e.currentTarget.checked),type:`checkbox`}),(0,O.jsx)(`span`,{children:`I understand certificate A1B2 is the only certificate Sideport is authorized to replace.`})]}),(0,O.jsx)(`button`,{className:`spc-button danger large`,disabled:!u,onClick:()=>s(`working`),type:`button`,children:`Replace signing identity`}),(0,O.jsx)(`button`,{className:`spc-text-button centered-button`,onClick:()=>s(`summary`),type:`button`,children:`Keep current signing`})]}):null,o===`working`?(0,O.jsxs)(`div`,{className:`centered`,children:[(0,O.jsx)(`div`,{className:`spc-pulse-icon`,children:(0,O.jsx)(p,{"aria-hidden":`true`,size:36})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Do not close Sideport`}),(0,O.jsx)(`h1`,{ref:h,tabIndex:-1,children:`Preparing the new signing identity`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Sideport is rechecking the certificate inventory, replacing only the approved certificate, and verifying the new identity.`}),(0,O.jsxs)(`div`,{className:`spc-wait-status`,children:[(0,O.jsx)(f,{"aria-hidden":`true`,size:18}),` Verifying with Apple…`]})]}):null,o===`done`?(0,O.jsxs)(`div`,{className:`centered`,children:[(0,O.jsx)(`div`,{className:`spc-hero-icon success`,children:(0,O.jsx)(i,{"aria-hidden":`true`,size:38})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Signing ready`}),(0,O.jsx)(`h1`,{ref:h,tabIndex:-1,children:`New signing identity verified`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Sideport replaced certificate A1B2 and saved the new identity. Installed apps remain available and use it on their next refresh.`}),(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:g,type:`button`,children:`Done`})]}):null]})}):null]})}function ve({onClose:e,onNavigate:t}){let n=(0,D.useRef)(null),r=(0,D.useRef)(null),[i,a]=(0,D.useState)(``);(0,D.useEffect)(()=>n.current?.focus(),[]);let o=De.filter(e=>`${e.eyebrow} ${e.label} ${e.detail}`.toLowerCase().includes(i.toLowerCase()));return(0,O.jsx)(`div`,{"aria-label":`Search Sideport`,"aria-modal":`true`,className:`spc-dialog-backdrop`,onKeyDown:t=>{if(t.key===`Escape`){e();return}if(t.key!==`Tab`)return;let n=Array.from(r.current?.querySelectorAll(`button:not([disabled]), input:not([disabled])`)??[]),i=n[0],a=n[n.length-1];!i||!a||(t.shiftKey&&document.activeElement===i?(t.preventDefault(),a.focus()):!t.shiftKey&&document.activeElement===a&&(t.preventDefault(),i.focus()))},ref:r,role:`dialog`,children:(0,O.jsxs)(`div`,{className:`spc-search-dialog`,children:[(0,O.jsxs)(`div`,{className:`spc-search-input`,children:[(0,O.jsx)(l,{"aria-hidden":`true`,size:20}),(0,O.jsx)(`label`,{className:`spc-visually-hidden`,htmlFor:`global-search`,children:`Search Sideport`}),(0,O.jsx)(`input`,{id:`global-search`,onChange:e=>a(e.currentTarget.value),placeholder:`Search apps, iPhones, people, and activity`,ref:n,type:`search`,value:i}),(0,O.jsx)(`button`,{"aria-label":`Close search`,className:`spc-icon-button`,onClick:e,type:`button`,children:(0,O.jsx)(y,{"aria-hidden":`true`,size:20})})]}),(0,O.jsxs)(`div`,{className:`spc-search-results`,children:[o.map(n=>{let r=n.icon;return(0,O.jsxs)(`button`,{onClick:()=>{t(n.route),e()},type:`button`,children:[(0,O.jsx)(`span`,{className:`spc-round-icon`,children:(0,O.jsx)(r,{"aria-hidden":`true`,size:20})}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`small`,{children:n.eyebrow}),(0,O.jsx)(`strong`,{children:n.label}),(0,O.jsx)(`span`,{children:n.detail})]}),(0,O.jsx)(d,{"aria-hidden":`true`,size:18})]},`${n.route}-${n.label}`)}),o.length?null:(0,O.jsx)(`p`,{children:`No matching apps, iPhones, people, or activity.`})]})]})})}function ye({memberName:e,role:t,initialRoute:n,onStartAssistant:r}){let[i,o]=(0,D.useState)(n),[s,c]=(0,D.useState)(!1),[u,d]=(0,D.useState)(!1),[f,p]=(0,D.useState)(!1),[_,ee]=(0,D.useState)(!1),ne=(0,D.useRef)(null),v=(0,D.useRef)(null),re=(0,D.useRef)(null),ie=(0,D.useRef)(null);(0,D.useEffect)(()=>{let e=e=>{(e.metaKey||e.ctrlKey)&&e.key.toLowerCase()===`k`&&(e.preventDefault(),c(!0))};return window.addEventListener(`keydown`,e),()=>window.removeEventListener(`keydown`,e)},[]),(0,D.useEffect)(()=>{_&&ie.current?.focus()},[_]);let oe=()=>{c(!1),ne.current?.focus()},b=e=>{o(e),d(!1),ee(!1)},se=e=>{re.current=e,d(!1),ee(!0)},ce=e=>{ee(!1),r(`connect`,`cert-clock`,e)};return(0,O.jsxs)(`div`,{className:`spc-shell`,"data-role":t,"data-testid":`canonical-signed-in-shell`,children:[(0,O.jsxs)(`aside`,{"aria-label":`Primary Sideport sidebar`,className:`spc-sidebar`,children:[(0,O.jsx)(w,{}),(0,O.jsx)(`nav`,{"aria-label":`Sideport navigation`,children:(0,O.jsx)(`ul`,{children:Ee.map(e=>{let t=e.icon;return(0,O.jsx)(`li`,{children:(0,O.jsxs)(`button`,{"aria-current":i===e.id?`page`:void 0,className:i===e.id?`active`:``,onClick:()=>b(e.id),type:`button`,children:[(0,O.jsx)(t,{"aria-hidden":`true`,size:19}),(0,O.jsx)(`span`,{children:e.label})]})},e.id)})})}),(0,O.jsxs)(`div`,{className:`spc-sidebar-footer`,children:[(0,O.jsx)(`span`,{className:`spc-avatar`,children:t===`owner`?`D`:`M`}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:t===`owner`?`Dragos`:`Mara`}),(0,O.jsx)(`small`,{children:t===`owner`?`Owner`:`Member`})]})]})]}),(0,O.jsxs)(`div`,{className:`spc-workspace`,children:[(0,O.jsxs)(`header`,{className:`spc-topbar`,children:[(0,O.jsx)(`div`,{className:`spc-mobile-brand`,children:(0,O.jsx)(w,{})}),(0,O.jsxs)(`button`,{"aria-label":`Search Sideport`,className:`spc-global-search`,onClick:()=>c(!0),ref:ne,type:`button`,children:[(0,O.jsx)(l,{"aria-hidden":`true`,size:18}),(0,O.jsx)(`span`,{children:`Search Sideport`}),(0,O.jsx)(`kbd`,{children:`⌘ K`})]}),t===`owner`?(0,O.jsxs)(`div`,{className:`spc-add-wrap`,onKeyDown:e=>{e.key===`Escape`&&u&&(d(!1),v.current?.focus())},children:[(0,O.jsxs)(`button`,{"aria-expanded":u,"aria-label":`Add`,className:`spc-button primary`,onClick:()=>d(e=>!e),ref:v,type:`button`,children:[(0,O.jsx)(m,{"aria-hidden":`true`,size:18}),` `,(0,O.jsx)(`span`,{children:`Add`})]}),u?(0,O.jsxs)(`div`,{"aria-label":`Add options`,className:`spc-add-menu`,role:`group`,children:[(0,O.jsxs)(`button`,{onClick:e=>se(e.currentTarget),type:`button`,children:[(0,O.jsx)(h,{"aria-hidden":`true`,size:19}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`Add iPhone`}),(0,O.jsx)(`small`,{children:`Choose its member, then pair once`})]})]}),(0,O.jsxs)(`button`,{onClick:()=>{o(`apps`),p(!0),d(!1)},type:`button`,children:[(0,O.jsx)(ae,{"aria-hidden":`true`,size:19}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`Import app`}),(0,O.jsx)(`small`,{children:`Computer, Sideport storage, or GitHub`})]})]}),(0,O.jsxs)(`button`,{onClick:()=>{o(`people`),d(!1)},type:`button`,children:[(0,O.jsx)(te,{"aria-hidden":`true`,size:19}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`Invite someone you trust`}),(0,O.jsx)(`small`,{children:`Create a private sign-in link`})]})]})]}):null]}):null,(0,O.jsx)(`button`,{"aria-label":`Open settings`,className:`spc-mobile-settings spc-icon-button`,onClick:()=>b(`settings`),type:`button`,children:(0,O.jsx)(a,{"aria-hidden":`true`,size:20})})]}),(0,O.jsx)(`nav`,{"aria-label":`Mobile Sideport navigation`,className:`spc-mobile-nav`,children:(0,O.jsx)(`ul`,{children:Ee.filter(e=>e.id!==`settings`).map(e=>{let t=e.icon;return(0,O.jsx)(`li`,{children:(0,O.jsxs)(`button`,{"aria-current":i===e.id?`page`:void 0,onClick:()=>b(e.id),type:`button`,children:[(0,O.jsx)(t,{"aria-hidden":`true`,size:20}),(0,O.jsx)(`span`,{children:e.label})]})},e.id)})})}),(0,O.jsxs)(`main`,{className:`spc-main`,children:[(0,O.jsxs)(`div`,{className:`spc-preview-line`,children:[(0,O.jsx)(S,{}),(0,O.jsx)(`span`,{children:`Storybook fixture · no live account, device, or app changes`})]}),_?(0,O.jsxs)(`section`,{className:`spc-target-picker`,"aria-labelledby":`phone-target-picker-title`,children:[(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Add iPhone`}),(0,O.jsx)(`h2`,{id:`phone-target-picker-title`,ref:ie,tabIndex:-1,children:`Who will use this iPhone?`}),(0,O.jsx)(`p`,{children:`Sideport checks that person’s active membership and Apple device capacity before pairing starts.`})]}),(0,O.jsxs)(`div`,{role:`group`,"aria-label":`iPhone member`,children:[(0,O.jsxs)(`button`,{className:`spc-button secondary`,onClick:()=>ce(`Mara`),type:`button`,children:[(0,O.jsx)(g,{"aria-hidden":`true`,size:17}),` Mara`]}),(0,O.jsxs)(`button`,{className:`spc-button secondary`,onClick:()=>ce(`Alex`),type:`button`,children:[(0,O.jsx)(g,{"aria-hidden":`true`,size:17}),` Alex`]}),(0,O.jsx)(`button`,{"aria-label":`Cancel member choice`,className:`spc-icon-button`,onClick:()=>{ee(!1),re.current?.focus()},type:`button`,children:(0,O.jsx)(y,{"aria-hidden":`true`,size:18})})]})]}):null,(()=>{switch(i){case`apps`:return(0,O.jsx)(pe,{importOpen:f,memberName:e,onInstall:(e,t)=>r(`install-waiting`,e,t),onToggleImport:()=>p(e=>!e),role:t});case`devices`:return(0,O.jsx)(me,{onAddIPhone:se,role:t});case`people`:return(0,O.jsx)(he,{role:t});case`activity`:return(0,O.jsx)(ge,{role:t});case`settings`:return(0,O.jsx)(_e,{role:t});default:return(0,O.jsx)(de,{onAddIPhone:se,onNavigate:b,role:t})}})()]})]}),s?(0,O.jsx)(ve,{onClose:oe,onNavigate:b}):null]})}function be({step:e}){let t=e===`connect`||e===`waiting`?1:e===`prepare`?2:3;return(0,O.jsxs)(`div`,{className:`spc-assistant-progress`,"aria-label":`Step ${t} of 3`,children:[(0,O.jsxs)(`span`,{children:[`Step `,t,` of 3`]}),(0,O.jsx)(`div`,{children:[1,2,3].map(e=>(0,O.jsx)(`i`,{className:e<=t?`active`:``},e))})]})}function xe({initialAppId:e=`cert-clock`,initialStep:t=`connect`,memberName:n,onClose:r,onFinish:a}){let[c,l]=(0,D.useState)(t),[d,p]=(0,D.useState)(e),m=(0,D.useRef)(null),g=k.find(e=>e.id===d)??k[0];return(0,D.useEffect)(()=>{m.current?.focus()},[c]),(0,D.useEffect)(()=>{if(c!==`waiting`&&c!==`install-waiting`&&c!==`installing`)return;let e=c===`waiting`?`prepare`:c===`install-waiting`?`installing`:`done`,t=window.setTimeout(()=>l(e),c===`installing`?1200:900);return()=>window.clearTimeout(t)},[c]),(0,O.jsxs)(`div`,{className:`spc-assistant`,"data-assistant-step":c,"data-testid":`canonical-iphone-assistant`,children:[(0,O.jsxs)(`header`,{className:`spc-assistant-header`,children:[(0,O.jsx)(w,{}),(0,O.jsx)(S,{}),r&&c!==`installing`?(0,O.jsx)(`button`,{"aria-label":`Close assistant`,className:`spc-icon-button`,onClick:r,type:`button`,children:(0,O.jsx)(y,{"aria-hidden":`true`,size:20})}):null]}),(0,O.jsxs)(`main`,{className:`spc-assistant-main`,children:[(0,O.jsx)(C,{}),c===`done`?null:(0,O.jsx)(be,{step:c}),c===`connect`?(0,O.jsxs)(`section`,{className:`spc-assistant-content`,children:[(0,O.jsx)(`div`,{className:`spc-hero-icon`,children:(0,O.jsx)(u,{"aria-hidden":`true`,size:38})}),(0,O.jsxs)(`span`,{className:`spc-eyebrow`,children:[`Add `,n,`’s iPhone`]}),(0,O.jsx)(`h1`,{ref:m,tabIndex:-1,children:`Connect the iPhone`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Use the cable beside the Sideport computer. Keep this page open—Sideport will find and add the iPhone automatically.`}),(0,O.jsxs)(`ol`,{className:`spc-phone-steps`,children:[(0,O.jsxs)(`li`,{children:[(0,O.jsx)(`span`,{children:`1`}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Plug in and unlock the iPhone`}),(0,O.jsx)(`small`,{children:`Leave it connected until Sideport says you can unplug.`})]})]}),(0,O.jsxs)(`li`,{children:[(0,O.jsx)(`span`,{children:`2`}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Tap “Trust” on the iPhone`}),(0,O.jsx)(`small`,{children:`Then enter the iPhone passcode. There is no Pair or Add button here.`})]})]}),(0,O.jsxs)(`li`,{children:[(0,O.jsx)(`span`,{children:`3`}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Keep the iPhone nearby`}),(0,O.jsx)(`small`,{children:`Next, Sideport will guide Developer Mode and the restart.`})]})]})]}),(0,O.jsxs)(`aside`,{className:`spc-inline-note`,children:[(0,O.jsx)(ie,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Why the cable?`}),(0,O.jsx)(`span`,{children:`It is required for first setup and installation. Later refreshes can use the same home Wi-Fi.`})]})]}),(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:()=>l(`waiting`),type:`button`,children:`Start connecting`})]}):null,c===`waiting`?(0,O.jsxs)(`section`,{className:`spc-assistant-content centered`,"aria-live":`polite`,children:[(0,O.jsx)(`div`,{className:`spc-pulse-icon`,children:(0,O.jsx)(h,{"aria-hidden":`true`,size:38})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Waiting for the iPhone`}),(0,O.jsx)(`h1`,{ref:m,tabIndex:-1,children:`Unlock it and tap Trust`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Sideport is watching the cable. When Trust is complete, pairing and adding happen automatically.`}),(0,O.jsxs)(`div`,{className:`spc-wait-status`,role:`status`,children:[(0,O.jsx)(f,{"aria-hidden":`true`,size:18}),` Looking for a trusted iPhone…`]})]}):null,c===`prepare`?(0,O.jsxs)(`section`,{className:`spc-assistant-content`,children:[(0,O.jsx)(`div`,{className:`spc-hero-icon success`,children:(0,O.jsx)(o,{"aria-hidden":`true`,size:38})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Simulated iPhone added automatically`}),(0,O.jsx)(`h1`,{ref:m,tabIndex:-1,children:`Turn on Developer Mode`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`This one iPhone setting allows approved Sideport apps to open. Apple requires a restart.`}),(0,O.jsxs)(`ol`,{className:`spc-phone-steps`,children:[(0,O.jsxs)(`li`,{children:[(0,O.jsx)(`span`,{children:`1`}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Open Settings → Privacy & Security`}),(0,O.jsx)(`small`,{children:`Scroll down and tap Developer Mode.`})]})]}),(0,O.jsxs)(`li`,{children:[(0,O.jsx)(`span`,{children:`2`}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Turn it on, then restart`}),(0,O.jsx)(`small`,{children:`The iPhone will ask you to confirm the restart.`})]})]}),(0,O.jsxs)(`li`,{children:[(0,O.jsx)(`span`,{children:`3`}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`After restart, unlock and tap Enable`}),(0,O.jsx)(`small`,{children:`Enter the passcode, then reconnect the cable if it was removed.`})]})]})]}),(0,O.jsxs)(`aside`,{className:`spc-inline-note warning`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Sideport cannot read this setting yet.`}),(0,O.jsx)(`span`,{children:`Continue only after you completed the steps on the iPhone.`})]})]}),(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:()=>l(`choose`),type:`button`,children:`I restarted and reconnected`})]}):null,c===`choose`?(0,O.jsxs)(`section`,{className:`spc-assistant-content wide`,children:[(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Ready to install`}),(0,O.jsx)(`h1`,{ref:m,tabIndex:-1,children:`Choose an app`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`These apps are approved by the Sideport owner. Choose one, then install once.`}),(0,O.jsx)(`div`,{className:`spc-assistant-apps`,role:`radiogroup`,"aria-label":`Approved apps`,children:k.map(e=>(0,O.jsxs)(`label`,{className:d===e.id?`selected`:``,children:[(0,O.jsx)(`input`,{checked:d===e.id,name:`assistant-app`,onChange:()=>p(e.id),type:`radio`,value:e.id}),(0,O.jsx)(ue,{app:e}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:e.name}),(0,O.jsx)(`small`,{children:e.description}),(0,O.jsxs)(`em`,{children:[`Version `,e.version,` · `,e.source]})]}),(0,O.jsx)(`span`,{className:`spc-radio-mark`,children:(0,O.jsx)(i,{"aria-hidden":`true`,size:15})})]},e.id))}),(0,O.jsxs)(`div`,{className:`spc-smart-default`,children:[(0,O.jsx)(f,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`Automatic refresh is on`}),(0,O.jsx)(`small`,{children:`Sideport attempts paired home Wi-Fi. Use the cable whenever a refresh cannot finish.`})]})]}),(0,O.jsxs)(`button`,{className:`spc-button primary large`,onClick:()=>l(`installing`),type:`button`,children:[`Install `,g.name]})]}):null,c===`install-waiting`?(0,O.jsxs)(`section`,{className:`spc-assistant-content centered`,"aria-live":`polite`,children:[(0,O.jsx)(`div`,{className:`spc-pulse-icon`,children:(0,O.jsx)(u,{"aria-hidden":`true`,size:38})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Simulated USB readiness check`}),(0,O.jsxs)(`h1`,{ref:m,tabIndex:-1,children:[`Connect the iPhone to install `,g.name]}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Plug it into the Sideport cable, unlock it, and tap Trust if asked. Sideport waits for the reliable USB connection and then starts automatically.`}),(0,O.jsxs)(`div`,{className:`spc-wait-status`,role:`status`,children:[(0,O.jsx)(f,{"aria-hidden":`true`,size:18}),` Waiting for the Sideport cable…`]})]}):null,c===`installing`?(0,O.jsxs)(`section`,{className:`spc-assistant-content centered`,"aria-live":`polite`,children:[(0,O.jsx)(`div`,{className:`spc-pulse-icon`,children:(0,O.jsx)(s,{"aria-hidden":`true`,size:38})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Keep the cable connected`}),(0,O.jsxs)(`h1`,{ref:m,tabIndex:-1,children:[`Installing `,g.name]}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Sideport is preparing, signing, installing, and checking the app on the iPhone. No more choices are needed.`}),(0,O.jsxs)(`div`,{"aria-label":`Install progress`,"aria-valuemax":100,"aria-valuemin":0,"aria-valuenow":75,className:`spc-install-track`,role:`progressbar`,children:[(0,O.jsx)(`span`,{}),(0,O.jsx)(`span`,{}),(0,O.jsx)(`span`,{})]}),(0,O.jsxs)(`div`,{className:`spc-wait-status`,children:[(0,O.jsx)(f,{"aria-hidden":`true`,size:18}),` Verifying the installed app…`]})]}):null,c===`done`?(0,O.jsxs)(`section`,{className:`spc-assistant-content centered success-screen`,children:[(0,O.jsx)(`div`,{className:`spc-success-orbit`,children:(0,O.jsx)(i,{"aria-hidden":`true`,size:42})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Simulated device-verified result`}),(0,O.jsx)(`h1`,{ref:m,tabIndex:-1,children:`Installed — you can unplug`}),(0,O.jsxs)(`p`,{className:`spc-lead`,children:[g.name,` `,g.version,` is ready on `,n,`’s iPhone. Sideport will attempt future refreshes over paired home Wi-Fi; reconnect the cable whenever one cannot finish.`]}),(0,O.jsxs)(`div`,{className:`spc-completion-list`,children:[(0,O.jsxs)(`span`,{children:[(0,O.jsx)(b,{"aria-hidden":`true`,size:19}),(0,O.jsx)(`strong`,{children:`Completion chime would play`}),(0,O.jsx)(`small`,{children:`Best effort when the browser allows audio`})]}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(ie,{"aria-hidden":`true`,size:19}),(0,O.jsx)(`strong`,{children:`Paired Wi-Fi attempted`}),(0,O.jsx)(`small`,{children:`Cable remains the reliable fallback`})]}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(x,{"aria-hidden":`true`,size:19}),(0,O.jsx)(`strong`,{children:`Device verification represented`}),(0,O.jsx)(`small`,{children:`Bundle, version, and expiry reread`})]})]}),(0,O.jsxs)(`aside`,{className:`spc-inline-note`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`When opening the app the first time`}),(0,O.jsx)(`span`,{children:`If iOS asks you to trust the developer profile, follow the message on the iPhone. Sideport verifies installation, not successful launch.`})]})]}),(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:a,type:`button`,children:`Open Sideport`})]}):null]})]})}function Se({memberName:e,onAccepted:t,onExistingSignIn:n,state:r}){let i=(0,D.useId)(),[a,o]=(0,D.useState)(`entry`),s={ready:{eyebrow:`Private Sideport invitation`,title:`Dragos invited you to Sideport`,lead:`Sideport installs approved apps on ${e}’s iPhone and attempts refreshes over paired home Wi-Fi, with the cable as the reliable fallback.`},expired:{eyebrow:`Invitation expired`,title:`Ask Dragos for a new link`,lead:`This private invitation is no longer valid. Sideport has not created an account or changed any device.`},used:{eyebrow:`Invitation already used`,title:`This invitation has already been used`,lead:`It cannot add another account. If you accepted it earlier, continue to Authentik; Sideport confirms the signed-in account before showing membership.`},suspended:{eyebrow:`Access paused`,title:`Your Sideport access is paused`,lead:`No new installs or account actions are available. Ask Dragos to review your Member access.`},recovery:{eyebrow:`Sign-in recovery`,title:`Recover your Authentik sign-in`,lead:`Authentik verifies your account and manages passkey recovery. Sideport cannot create, reset, or read your passkey.`}};if(r===`ready`&&a===`confirm`)return(0,O.jsxs)(`div`,{className:`spc-invitation`,"data-testid":`canonical-member-invitation`,children:[(0,O.jsxs)(`header`,{children:[(0,O.jsx)(w,{}),(0,O.jsx)(S,{})]}),(0,O.jsxs)(`main`,{children:[(0,O.jsx)(C,{}),(0,O.jsx)(`div`,{className:`spc-invite-illustration`,children:(0,O.jsx)(g,{"aria-hidden":`true`,size:44})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Signed in through Authentik`}),(0,O.jsx)(`h1`,{id:i,children:`Join Dragos’s Sideport?`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Check the account and access below before this invitation is used.`}),(0,O.jsxs)(`div`,{className:`spc-passkey-card`,children:[(0,O.jsx)(g,{"aria-hidden":`true`,size:24}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Mara · mara@example.test`}),(0,O.jsx)(`span`,{children:`This is the account that will receive Member access.`})]})]}),(0,O.jsxs)(`div`,{className:`spc-passkey-card`,children:[(0,O.jsx)(x,{"aria-hidden":`true`,size:24}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Member access`}),(0,O.jsx)(`span`,{children:`Install approved apps and add your first iPhone. Apple signing, app sources, and other people’s devices stay with Dragos.`})]})]}),(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:t,type:`button`,children:`Join Sideport`}),(0,O.jsx)(`button`,{className:`spc-text-button centered-button`,onClick:()=>o(`entry`),type:`button`,children:`Use a different account`}),(0,O.jsx)(`p`,{className:`spc-fine-print`,children:`This confirmation uses an opaque, short-lived handoff. The private invitation is not kept in browser storage.`})]})]});let c=s[r],l=r===`ready`||r===`used`||r===`recovery`;return(0,O.jsxs)(`div`,{className:`spc-invitation`,"data-testid":`canonical-member-invitation`,children:[(0,O.jsxs)(`header`,{children:[(0,O.jsx)(w,{}),(0,O.jsx)(S,{})]}),(0,O.jsxs)(`main`,{children:[(0,O.jsx)(C,{}),(0,O.jsx)(`div`,{className:`spc-invite-illustration`,children:(0,O.jsx)(te,{"aria-hidden":`true`,size:44})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:c.eyebrow}),(0,O.jsx)(`h1`,{id:i,children:c.title}),(0,O.jsx)(`p`,{className:`spc-lead`,children:c.lead}),l?(0,O.jsxs)(`div`,{className:`spc-passkey-card`,children:[(0,O.jsx)(x,{"aria-hidden":`true`,size:24}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:r===`recovery`?`Recovery stays with Authentik`:`Continue to Authentik`}),(0,O.jsx)(`span`,{children:`Authentik may use Face ID, Touch ID, Windows Hello, or your password manager. Sideport never receives your Apple password or passkey.`})]})]}):null,r===`ready`?(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:()=>o(`confirm`),type:`button`,children:`Continue to sign in`}):null,r===`recovery`?(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:n,type:`button`,children:`Continue to sign-in recovery`}):null,r===`used`?(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:n,type:`button`,children:`Continue to sign in`}):null,r===`expired`?(0,O.jsxs)(`aside`,{className:`spc-inline-note`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`A new invitation is required.`}),(0,O.jsx)(`span`,{children:`Only the Sideport owner can create it.`})]})]}):null,r===`suspended`?(0,O.jsxs)(`aside`,{className:`spc-inline-note warning`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`The owner must restore access.`}),(0,O.jsx)(`span`,{children:`A new invitation cannot bypass a suspension.`})]})]}):null,l?(0,O.jsx)(`p`,{className:`spc-fine-print`,children:`Authentik owns sign-in and recovery. This is not official “Sign in with Apple.”`}):null]})]})}function Ce({onAccepted:e,state:t}){let[n,r]=(0,D.useState)(!1),[i,a]=(0,D.useState)(``),[o,s]=(0,D.useState)(``);return t===`recovery`?(0,O.jsxs)(`div`,{className:`spc-invitation`,"data-testid":`canonical-owner-claim`,children:[(0,O.jsxs)(`header`,{children:[(0,O.jsx)(w,{}),(0,O.jsx)(S,{})]}),(0,O.jsxs)(`main`,{children:[(0,O.jsx)(C,{}),(0,O.jsx)(`div`,{className:`spc-invite-illustration`,children:(0,O.jsx)(x,{"aria-hidden":`true`,size:44})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:n?`Signed in through Authentik`:`Private owner recovery link`}),(0,O.jsx)(`h1`,{children:n?`Recover owner access?`:`Recover Sideport owner access`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:n?`Confirm the signed-in account before Sideport changes owner access.`:`This short-lived, single-use link was created from the Sideport host. The long-lived recovery key stays out of the browser.`}),n?(0,O.jsxs)(O.Fragment,{children:[(0,O.jsxs)(`div`,{className:`spc-passkey-card`,children:[(0,O.jsx)(g,{"aria-hidden":`true`,size:24}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Mara · mara@example.test`}),(0,O.jsx)(`span`,{children:`This Authentik account will be the one active Sideport owner.`})]})]}),(0,O.jsxs)(`div`,{className:`spc-passkey-card`,children:[(0,O.jsx)(x,{"aria-hidden":`true`,size:24}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Owner access`}),(0,O.jsx)(`span`,{children:`Manage member access, Apple signing, approved apps, every iPhone, and technical settings.`})]})]}),(0,O.jsxs)(`aside`,{className:`spc-inline-note warning`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Dragos will lose Owner access and be signed out.`}),(0,O.jsx)(`span`,{children:`2 Members, 2 iPhones, 3 installed apps, and their activity stay in Sideport. No app is removed and automatic refresh history is retained.`})]})]}),(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:e,type:`button`,children:`Recover owner access`}),(0,O.jsx)(`button`,{className:`spc-text-button centered-button`,onClick:()=>r(!1),type:`button`,children:`Use a different account`})]}):(0,O.jsxs)(O.Fragment,{children:[(0,O.jsxs)(`aside`,{className:`spc-inline-note warning`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`The current owner will be signed out.`}),(0,O.jsx)(`span`,{children:`Apps, iPhones, signing state, and activity are retained. Review the exact impact before the host creates this link.`})]})]}),(0,O.jsxs)(`div`,{className:`spc-passkey-card`,children:[(0,O.jsx)(x,{"aria-hidden":`true`,size:24}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Continue to Authentik`}),(0,O.jsx)(`span`,{children:`Authentik proves your account. Sideport then asks once more before granting Owner access.`})]})]}),(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:()=>r(!0),type:`button`,children:`Continue to sign in`})]}),(0,O.jsx)(`p`,{className:`spc-fine-print`,children:`No API key, Apple password, or passkey is entered on this page.`})]})]}):(0,O.jsxs)(`div`,{className:`spc-invitation`,"data-testid":`canonical-owner-claim`,children:[(0,O.jsxs)(`header`,{children:[(0,O.jsx)(w,{}),(0,O.jsx)(S,{})]}),(0,O.jsxs)(`main`,{children:[(0,O.jsx)(C,{}),(0,O.jsx)(`div`,{className:`spc-invite-illustration`,children:(0,O.jsx)(x,{"aria-hidden":`true`,size:44})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`First setup`}),(0,O.jsx)(`h1`,{children:`Finish setting up Sideport`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Create the Owner passkey on this device. There is no setup token or link to copy.`}),(0,O.jsxs)(`form`,{className:`spc-identity-form`,onSubmit:t=>{t.preventDefault(),e()},children:[(0,O.jsxs)(`label`,{children:[(0,O.jsx)(`span`,{children:`Name`}),(0,O.jsx)(`input`,{autoComplete:`name`,onChange:e=>a(e.currentTarget.value),placeholder:`Your name`,value:i})]}),(0,O.jsxs)(`label`,{children:[(0,O.jsx)(`span`,{children:`Email`}),(0,O.jsx)(`input`,{autoComplete:`email`,inputMode:`email`,onChange:e=>s(e.currentTarget.value),placeholder:`you@example.com`,type:`email`,value:o})]}),(0,O.jsx)(`button`,{className:`spc-button primary large`,disabled:!i.trim()||!o.trim(),type:`submit`,children:`Create passkey`})]}),(0,O.jsx)(`p`,{className:`spc-fine-print`,children:`Your passkey stays on your trusted device. Sideport never receives your Apple password.`})]})]})}function we({onContinue:e}){let[t,n]=(0,D.useState)(!1),[r,a]=(0,D.useState)(!1),[o,s]=(0,D.useState)(`owner@example.test`),[c,l]=(0,D.useState)(`storybook-demo`),f=(0,D.useRef)(null);return(0,D.useEffect)(()=>{f.current?.focus()},[r]),(0,O.jsxs)(`div`,{className:`spc-first-run`,"data-testid":`canonical-first-run`,children:[(0,O.jsxs)(`header`,{children:[(0,O.jsx)(w,{}),(0,O.jsx)(S,{})]}),(0,O.jsxs)(`main`,{children:[(0,O.jsx)(C,{}),r?(0,O.jsxs)(`form`,{className:`spc-apple-form`,onSubmit:t=>{t.preventDefault(),l(``),e()},children:[(0,O.jsx)(`div`,{className:`spc-hero-icon`,children:(0,O.jsx)(p,{"aria-hidden":`true`,size:38})}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`Owner signing access`}),(0,O.jsx)(`h1`,{ref:f,tabIndex:-1,children:`Connect your Apple account`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`Sideport uses this Apple Developer access to register iPhones, manage App IDs, certificates, and profiles, and sign approved apps. The protected credential stays in server-side custody and is separate from member sign-in.`}),(0,O.jsxs)(`div`,{className:`spc-inline-note warning`,id:`storybook-credential-warning`,role:`note`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Demo only — do not enter real credentials.`}),(0,O.jsx)(`span`,{children:`These example fields make no request and store nothing.`})]})]}),(0,O.jsx)(`label`,{htmlFor:`owner-apple-email`,children:`Demo Apple Account email`}),(0,O.jsx)(`input`,{"aria-describedby":`storybook-credential-warning`,autoComplete:`off`,id:`owner-apple-email`,onChange:e=>s(e.currentTarget.value),type:`email`,value:o}),(0,O.jsx)(`label`,{htmlFor:`owner-apple-password`,children:`Demo password`}),(0,O.jsx)(`input`,{"aria-describedby":`storybook-credential-warning`,autoComplete:`off`,id:`owner-apple-password`,onChange:e=>l(e.currentTarget.value),type:`password`,value:c}),(0,O.jsxs)(`div`,{className:`spc-inline-note`,role:`note`,children:[(0,O.jsx)(p,{"aria-hidden":`true`,size:18}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`strong`,{children:`Team selection is automatic.`}),(0,O.jsx)(`span`,{children:`Sideport uses the only team Apple returns. A chooser appears only if Apple returns more than one.`})]})]}),(0,O.jsx)(`button`,{className:`spc-button primary large`,disabled:!o||!c,type:`submit`,children:`Continue demo`}),(0,O.jsx)(`button`,{className:`spc-text-button centered-button`,onClick:()=>{l(`storybook-demo`),a(!1)},type:`button`,children:`Back`})]}):(0,O.jsxs)(O.Fragment,{children:[(0,O.jsxs)(`div`,{className:`spc-setup-visual`,children:[(0,O.jsx)(_,{"aria-hidden":`true`,size:42}),(0,O.jsx)(`span`,{children:(0,O.jsx)(i,{"aria-hidden":`true`,size:18})})]}),(0,O.jsx)(`span`,{className:`spc-eyebrow`,children:`First-time setup`}),(0,O.jsx)(`h1`,{ref:f,tabIndex:-1,children:`Welcome to Sideport`}),(0,O.jsx)(`p`,{className:`spc-lead`,children:`This home server is ready. Connect one Apple account for signing, then use the nearby cable to install the first app.`}),(0,O.jsxs)(`section`,{className:`spc-readiness-list`,"aria-label":`Setup readiness`,children:[(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{className:`spc-round-icon success`,children:(0,O.jsx)(i,{"aria-hidden":`true`,size:20})}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`Sideport is running`}),(0,O.jsx)(`small`,{children:`Saved data and iPhone trust survive restarts.`})]}),(0,O.jsx)(E,{children:`Ready`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{className:`spc-round-icon`,children:(0,O.jsx)(p,{"aria-hidden":`true`,size:20})}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`Apple signing`}),(0,O.jsx)(`small`,{children:`Use the owner’s Apple account; members never see it.`})]}),(0,O.jsx)(E,{tone:`warning`,children:`Next`})]}),(0,O.jsxs)(`div`,{children:[(0,O.jsx)(`span`,{className:`spc-round-icon`,children:(0,O.jsx)(u,{"aria-hidden":`true`,size:20})}),(0,O.jsxs)(`span`,{children:[(0,O.jsx)(`strong`,{children:`First iPhone and app`}),(0,O.jsx)(`small`,{children:`The same guided cable assistant handles both.`})]}),(0,O.jsx)(E,{tone:`quiet`,children:`Later`})]})]}),(0,O.jsxs)(`button`,{"aria-expanded":t,className:`spc-disclosure`,onClick:()=>n(e=>!e),type:`button`,children:[(0,O.jsx)(v,{"aria-hidden":`true`,size:17}),` `,t?`Hide installation details`:`How this installation is saved`,` `,(0,O.jsx)(d,{"aria-hidden":`true`,size:17})]}),t?(0,O.jsxs)(`section`,{className:`spc-technical-panel`,children:[(0,O.jsx)(`h2`,{children:`Installation details`}),(0,O.jsx)(`p`,{children:`Docker keeps Sideport state, working files, anisette identity, and iPhone pairing records in persistent storage. The proposed Apple Container path is experimental and remains unverified until Phase 15; it is expected to use the official CLI network, the existing amd64 image under Rosetta, native anisette, persistent state roots, and the host usbmuxd socket. Sideport does not yet claim working Apple Container device installation, raw USB passthrough, or native arm64 support.`})]}):null,(0,O.jsx)(`button`,{className:`spc-button primary large`,onClick:()=>a(!0),type:`button`,children:`Connect Apple account`})]})]})]})}function Te({role:e=`owner`,experience:t=`shell`,initialRoute:n=`home`,initialAssistantStep:r=`connect`,invitationState:i=`ready`,ownerClaimState:a=`setup`,memberName:o=`Mara`}){let[s,c]=(0,D.useState)(t),[l,u]=(0,D.useState)(r),[d,f]=(0,D.useState)(`cert-clock`),[p,m]=(0,D.useState)(o),[h,g]=(0,D.useState)(e),_=(e=`connect`,t=`cert-clock`,n=o)=>{u(e),f(t),m(n),c(`add-iphone`)};return s===`invitation`?(0,O.jsx)(Se,{memberName:o,onAccepted:()=>{g(`member`),_(`connect`)},onExistingSignIn:()=>{g(`member`),c(`shell`)},state:i}):s===`owner-claim`?(0,O.jsx)(Ce,{onAccepted:()=>{g(`owner`),c(a===`setup`?`first-run`:`shell`)},state:a}):s===`first-run`?(0,O.jsx)(we,{onContinue:()=>{g(`owner`),_(`connect`)}}):s===`add-iphone`?(0,O.jsx)(xe,{initialAppId:d,initialStep:l,memberName:p,onClose:()=>c(`shell`),onFinish:()=>c(`shell`)}):(0,O.jsx)(ye,{initialRoute:n,memberName:o,onStartAssistant:_,role:h})}var D,O,Ee,k,De,Oe=e((()=>{D=t(r(),1),ce(),le(),O=n(),Ee=[{id:`home`,label:`Home`,icon:oe},{id:`apps`,label:`Apps`,icon:s},{id:`devices`,label:`Devices`,icon:h},{id:`people`,label:`People`,icon:te},{id:`activity`,label:`Activity`,icon:c},{id:`settings`,label:`Settings`,icon:a}],k=[{id:`cert-clock`,name:`Cert Clock`,description:`See when your installed apps need a refresh.`,version:`0.1.0`,initials:`CC`,tone:`blue`,source:`On this Sideport`,installedOn:`3 iPhones`,release:`Up to date`},{id:`dice-roll`,name:`Dice Roll`,description:`A small one-tap dice roller for people you trust.`,version:`0.1.0`,initials:`DR`,tone:`amber`,source:`GitHub release`,installedOn:`2 iPhones`,release:`Update available`},{id:`concentration`,name:`Concentration`,description:`A simple card-matching memory game.`,version:`0.1.0`,initials:`CO`,tone:`green`,source:`GitHub release`,installedOn:`1 iPhone`,release:`Ready to install`}],De=[{route:`apps`,eyebrow:`App`,label:`Cert Clock`,detail:`Version 0.1.0 · ready to install`,icon:s},{route:`devices`,eyebrow:`iPhone`,label:`Mara’s iPhone`,detail:`Connected over home Wi-Fi`,icon:h},{route:`people`,eyebrow:`Member`,label:`Mara`,detail:`Active · 1 iPhone`,icon:g},{route:`activity`,eyebrow:`Activity`,label:`Cert Clock refreshed`,detail:`Today at 09:42`,icon:f}],Te.__docgenInfo={description:``,methods:[],displayName:`CanonicalSideport`,props:{role:{required:!1,tsType:{name:`union`,raw:`'owner' | 'member'`,elements:[{name:`literal`,value:`'owner'`},{name:`literal`,value:`'member'`}]},description:``,defaultValue:{value:`'owner'`,computed:!1}},experience:{required:!1,tsType:{name:`union`,raw:`'shell' | 'invitation' | 'owner-claim' | 'first-run' | 'add-iphone'`,elements:[{name:`literal`,value:`'shell'`},{name:`literal`,value:`'invitation'`},{name:`literal`,value:`'owner-claim'`},{name:`literal`,value:`'first-run'`},{name:`literal`,value:`'add-iphone'`}]},description:``,defaultValue:{value:`'shell'`,computed:!1}},initialRoute:{required:!1,tsType:{name:`union`,raw:`'home' | 'apps' | 'devices' | 'people' | 'activity' | 'settings'`,elements:[{name:`literal`,value:`'home'`},{name:`literal`,value:`'apps'`},{name:`literal`,value:`'devices'`},{name:`literal`,value:`'people'`},{name:`literal`,value:`'activity'`},{name:`literal`,value:`'settings'`}]},description:``,defaultValue:{value:`'home'`,computed:!1}},initialAssistantStep:{required:!1,tsType:{name:`union`,raw:`'connect' | 'waiting' | 'prepare' | 'choose' | 'install-waiting' | 'installing' | 'done'`,elements:[{name:`literal`,value:`'connect'`},{name:`literal`,value:`'waiting'`},{name:`literal`,value:`'prepare'`},{name:`literal`,value:`'choose'`},{name:`literal`,value:`'install-waiting'`},{name:`literal`,value:`'installing'`},{name:`literal`,value:`'done'`}]},description:``,defaultValue:{value:`'connect'`,computed:!1}},invitationState:{required:!1,tsType:{name:`union`,raw:`'ready' | 'expired' | 'used' | 'suspended' | 'recovery'`,elements:[{name:`literal`,value:`'ready'`},{name:`literal`,value:`'expired'`},{name:`literal`,value:`'used'`},{name:`literal`,value:`'suspended'`},{name:`literal`,value:`'recovery'`}]},description:``,defaultValue:{value:`'ready'`,computed:!1}},ownerClaimState:{required:!1,tsType:{name:`union`,raw:`'setup' | 'recovery'`,elements:[{name:`literal`,value:`'setup'`},{name:`literal`,value:`'recovery'`}]},description:``,defaultValue:{value:`'setup'`,computed:!1}},memberName:{required:!1,tsType:{name:`string`},description:``,defaultValue:{value:`'Mara'`,computed:!1}}}}})),A,j,M,N,ke,P,F,I,Ae,L,R,z,B,V,H,U,W,G,K,q,J,Y,X,Z,Q,je,Me,Ne,Pe,Fe,$,Ie;e((()=>{Oe(),{expect:A,userEvent:j,waitFor:M,within:N}=__STORYBOOK_MODULE_TEST__,ke={title:`Sideport/Canonical Product`,component:Te,parameters:{docs:{description:{component:`Canonical Storybook-only proposal for Sideport’s six-destination trusted-people product, secure owner/invitation handoff, and one-cable iPhone journey. Fixtures make no live account, device, app, or infrastructure changes.`}}}},P={name:`01 Shell · owner home`,args:{experience:`shell`,role:`owner`,initialRoute:`home`},play:async({canvasElement:e})=>{let t=N(e),n=N(t.getByTestId(`canonical-signed-in-shell`)).getByRole(`navigation`,{name:`Sideport navigation`});await A(N(n).getAllByRole(`button`).map(e=>e.textContent?.trim())).toEqual([`Home`,`Apps`,`Devices`,`People`,`Activity`,`Settings`]),await A(N(n).queryByRole(`button`,{name:/Onboarding|Renewals|Operations|Diagnostics|Apple Access|Teams|Users|Install App/i})).not.toBeInTheDocument(),await A(t.getByRole(`heading`,{name:`Apps and iPhones at a glance`})).toBeVisible(),await A(t.getByText(`Cable ready for any trusted iPhone`)).toBeVisible(),await A(t.getByText(`1 update available`)).toBeVisible(),await A(t.getByText(/Storybook fixture/)).toBeVisible()}},F={name:`13 Settings · exact signing replacement impact`,args:{experience:`shell`,role:`owner`,initialRoute:`settings`},play:async({canvasElement:e})=>{let t=N(e),n=N(e.ownerDocument.body);await j.click(t.getByRole(`button`,{name:`Review Apple signing`}));let r=n.getByRole(`dialog`,{name:`Review Apple signing`});await A(N(r).getByText(/one Apple account and one team/)).toBeVisible(),await j.click(N(r).getByRole(`button`,{name:`Change account or team`})),await A(N(r).getByLabelText(`Password`)).toHaveAttribute(`autocomplete`,`current-password`),await j.click(N(r).getByRole(`button`,{name:`Check current signing`})),await A(N(r).getByText(`Certificate ending A1B2 will be revoked`)).toBeVisible(),await A(N(r).getByRole(`button`,{name:`Replace signing identity`})).toBeDisabled(),await j.click(N(r).getByRole(`checkbox`)),await j.click(N(r).getByRole(`button`,{name:`Replace signing identity`})),await M(()=>A(N(r).getByRole(`heading`,{name:`New signing identity verified`})).toBeVisible(),{timeout:2e3})}},I={name:`02 Shell · member scope and safe projections`,args:{experience:`shell`,role:`member`,initialRoute:`home`,memberName:`Mara`},play:async({canvasElement:e})=>{let t=N(e),n=t.getByRole(`navigation`,{name:`Sideport navigation`});await A(N(n).getAllByRole(`button`)).toHaveLength(6),await A(t.queryByRole(`button`,{name:`Add`})).not.toBeInTheDocument(),await A(t.queryByRole(`button`,{name:/Add another iPhone/})).not.toBeInTheDocument(),await j.click(N(n).getByRole(`button`,{name:`Devices`})),await A(t.queryByRole(`button`,{name:`Add iPhone`})).not.toBeInTheDocument(),await A(t.getByText(`Need another iPhone?`)).toBeVisible(),await j.click(N(n).getByRole(`button`,{name:`Activity`})),await A(t.queryByRole(`button`,{name:/technical details/i})).not.toBeInTheDocument(),await A(t.queryByText(/network-usbmux|onboarding_v2|op_refresh_01/)).not.toBeInTheDocument(),await j.click(N(n).getByRole(`button`,{name:`Settings`})),await A(t.getByRole(`heading`,{name:`Settings`})).toBeVisible(),await A(t.queryByRole(`heading`,{name:`Signing`})).not.toBeInTheDocument()}},Ae={name:`03 Setup · fresh deployment outside shell`,args:{experience:`first-run`,role:`owner`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Welcome to Sideport`})).toBeVisible(),await A(t.queryByRole(`navigation`,{name:`Sideport navigation`})).not.toBeInTheDocument(),await A(t.getByText(`Sideport is running`)).toBeVisible(),await A(t.getByText(/Apple signing/)).toBeVisible(),await A(t.getByText(/First iPhone and app/)).toBeVisible(),await j.click(t.getByRole(`button`,{name:`How this installation is saved`})),await A(t.getByText(/Docker keeps Sideport state/)).toBeVisible(),await A(t.getByText(/proposed Apple Container path is experimental/)).toBeVisible(),await j.click(t.getByRole(`button`,{name:`Hide installation details`}))}},L={name:`02b Setup · direct Owner passkey`,args:{experience:`owner-claim`,ownerClaimState:`setup`,role:`owner`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Finish setting up Sideport`})).toBeVisible(),await A(t.getByText(/no setup token or link to copy/)).toBeVisible(),await A(t.queryByLabelText(/API key|recovery key/i)).not.toBeInTheDocument();let n=t.getByRole(`button`,{name:`Create passkey`});await A(n).toBeDisabled(),await j.type(t.getByRole(`textbox`,{name:`Name`}),`Dragos`),await j.type(t.getByRole(`textbox`,{name:`Email`}),`dragos@example.test`),await A(n).toBeEnabled(),await j.click(n),await A(t.getByRole(`heading`,{name:`Welcome to Sideport`})).toBeVisible()}},R={name:`02b.1 Setup · owner claim preview`,args:{experience:`owner-claim`,ownerClaimState:`setup`,role:`owner`}},z={name:`02c Recovery · replace inaccessible owner`,args:{experience:`owner-claim`,ownerClaimState:`recovery`,role:`owner`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Recover Sideport owner access`})).toBeVisible(),await A(t.getByText(`The current owner will be signed out.`)).toBeVisible(),await A(t.getByText(/Apps, iPhones, signing state, and activity are retained/)).toBeVisible(),await j.click(t.getByRole(`button`,{name:`Continue to sign in`})),await A(t.getByRole(`heading`,{name:`Recover owner access?`})).toBeVisible(),await A(t.getByText(`Mara · mara@example.test`)).toBeVisible(),await A(t.getByText(/Dragos will lose Owner access and be signed out/)).toBeVisible(),await A(t.getByText(/2 Members, 2 iPhones, 3 installed apps/)).toBeVisible(),await j.click(t.getByRole(`button`,{name:`Recover owner access`})),await A(t.getByRole(`heading`,{name:`Apps and iPhones at a glance`})).toBeVisible()}},B={name:`02c.1 Recovery · owner claim preview`,args:{experience:`owner-claim`,ownerClaimState:`recovery`,role:`owner`}},V={name:`04 Setup · owner signing is not member login`,args:{experience:`first-run`,role:`owner`},play:async({canvasElement:e})=>{let t=N(e);await j.click(t.getByRole(`button`,{name:`Connect Apple account`})),await A(t.getByRole(`heading`,{name:`Connect your Apple account`})).toBeVisible(),await A(t.getByText(/separate from member sign-in/)).toBeVisible(),await A(t.getByText(/Team selection is automatic/)).toBeVisible(),await A(t.getByText(/do not enter real credentials/)).toBeVisible(),await A(t.getByLabelText(`Demo Apple Account email`)).toHaveAttribute(`autocomplete`,`off`),await A(t.getByLabelText(`Demo password`)).toHaveAttribute(`autocomplete`,`off`),await A(t.getByRole(`button`,{name:`Continue demo`})).toBeEnabled()}},H={name:`05 People · invitation and signed-account consent`,args:{experience:`invitation`,role:`member`,memberName:`Mara`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Dragos invited you to Sideport`})).toBeVisible(),await A(t.getByText(/Face ID, Touch ID, Windows Hello/)).toBeVisible(),await A(t.getByText(/not official/)).toBeVisible(),await A(t.getByText(/No invitation, sign-in, account, device, app, or audio action occurs/)).toBeVisible(),await A(t.queryByLabelText(/password/i)).not.toBeInTheDocument(),await A(t.queryByRole(`navigation`,{name:`Sideport navigation`})).not.toBeInTheDocument(),await j.click(t.getByRole(`button`,{name:`Continue to sign in`})),await A(t.getByRole(`heading`,{name:`Join Dragos’s Sideport?`})).toBeVisible(),await A(t.getByText(`Mara · mara@example.test`)).toBeVisible(),await A(t.getByRole(`button`,{name:`Join Sideport`})).toBeVisible(),await A(t.getByText(/not kept in browser storage/)).toBeVisible()}},U={name:`05b People · invitation expired`,args:{experience:`invitation`,invitationState:`expired`,role:`member`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Ask Dragos for a new link`})).toBeVisible(),await A(t.getByText(`A new invitation is required.`)).toBeVisible(),await A(t.queryByRole(`button`,{name:/passkey/i})).not.toBeInTheDocument()}},W={name:`05c People · invitation already used`,args:{experience:`invitation`,invitationState:`used`,role:`member`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`This invitation has already been used`})).toBeVisible(),await A(t.getByRole(`button`,{name:`Continue to sign in`})).toBeVisible(),await A(t.getByText(/confirms the signed-in account before showing membership/)).toBeVisible()}},G={name:`05d People · access suspended`,args:{experience:`invitation`,invitationState:`suspended`,role:`member`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Your Sideport access is paused`})).toBeVisible(),await A(t.getByText(`The owner must restore access.`)).toBeVisible(),await A(t.queryByRole(`button`,{name:/passkey/i})).not.toBeInTheDocument()}},K={name:`05e People · Authentik-owned recovery`,args:{experience:`invitation`,invitationState:`recovery`,role:`member`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Recover your Authentik sign-in`})).toBeVisible(),await A(t.getByRole(`button`,{name:`Continue to sign-in recovery`})).toBeVisible(),await A(t.getByText(/Sideport cannot create, reset, or read your passkey/)).toBeVisible(),await j.click(t.getByRole(`button`,{name:`Continue to sign-in recovery`})),await A(t.getByRole(`heading`,{name:`Your apps are ready`})).toBeVisible(),await A(t.queryByRole(`heading`,{name:`Connect the iPhone`})).not.toBeInTheDocument()}},q={name:`06 Member · invite to verified install`,args:{experience:`invitation`,role:`member`,memberName:`Mara`},play:async({canvasElement:e})=>{let t=N(e);await j.click(t.getByRole(`button`,{name:`Continue to sign in`})),await A(t.getByRole(`heading`,{name:`Join Dragos’s Sideport?`})).toBeVisible(),await j.click(t.getByRole(`button`,{name:`Join Sideport`})),await A(t.getByRole(`heading`,{name:`Connect the iPhone`})).toBeVisible(),await A(t.getByText(/Tap “Trust” on the iPhone/)).toBeVisible(),await A(t.queryByRole(`button`,{name:/Pair|Add to Sideport/})).not.toBeInTheDocument(),await j.click(t.getByRole(`button`,{name:`Start connecting`})),await A(t.getByRole(`heading`,{name:`Unlock it and tap Trust`})).toBeVisible(),await M(()=>A(t.getByRole(`heading`,{name:`Turn on Developer Mode`})).toBeVisible(),{timeout:2500}),await j.click(t.getByRole(`button`,{name:`I restarted and reconnected`})),await A(t.getByRole(`heading`,{name:`Choose an app`})).toBeVisible(),await A(t.getByText(`Automatic refresh is on`)).toBeVisible(),await j.click(t.getByRole(`radio`,{name:/Dice Roll/})),await j.click(t.getByRole(`button`,{name:`Install Dice Roll`})),await A(t.getByRole(`heading`,{name:`Installing Dice Roll`})).toBeVisible(),await M(()=>A(t.getByRole(`heading`,{name:`Installed — you can unplug`})).toBeVisible(),{timeout:3e3}),await A(t.getByText(/Paired Wi-Fi attempted/)).toBeVisible(),await A(t.getByText(/Cable remains the reliable fallback/)).toBeVisible(),await A(t.getByText(/Sideport verifies installation, not successful launch/)).toBeVisible(),await j.click(t.getByRole(`button`,{name:`Open Sideport`})),await A(t.getByRole(`navigation`,{name:`Sideport navigation`})).toBeVisible(),await A(t.getByRole(`heading`,{name:`Your apps are ready`})).toBeVisible()}},J={name:`07 Add · one shared iPhone assistant`,args:{experience:`shell`,role:`owner`,initialRoute:`activity`},play:async({canvasElement:e})=>{let t=N(e),n=t.getByRole(`button`,{name:`Add`});await j.click(n),await j.keyboard(`{Escape}`),await M(()=>A(n).toHaveFocus()),await j.click(n),await j.click(t.getByRole(`button`,{name:/Add iPhone/})),await A(t.getByRole(`heading`,{name:`Who will use this iPhone?`})).toBeVisible(),await A(t.queryByRole(`heading`,{name:`Connect the iPhone`})).not.toBeInTheDocument(),await j.click(t.getByRole(`button`,{name:`Mara`})),await A(t.getByRole(`heading`,{name:`Connect the iPhone`})).toBeVisible(),await A(t.queryByRole(`navigation`,{name:`Sideport navigation`})).not.toBeInTheDocument(),await A(t.getByText(`Step 1 of 3`)).toBeVisible()}},Y={name:`08 Apps · approved library and sources`,args:{experience:`shell`,role:`owner`,initialRoute:`apps`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Apps`})).toBeVisible(),await A(t.getAllByRole(`button`,{name:`Choose iPhone`})).toHaveLength(3),await A(t.getByText(/Version 0.1.0 · On this Sideport/)).toBeVisible(),await A(t.getAllByText(/Version 0.1.0 · GitHub release/)).toHaveLength(2),await A(t.getByRole(`button`,{name:/Import app/})).toBeVisible(),await j.click(t.getByRole(`button`,{name:/Import app/}));let n=t.getByRole(`group`,{name:`IPA source`});await A(N(n).getByRole(`button`,{name:/This computer/})).toBeVisible(),await A(N(n).getByRole(`button`,{name:/On this Sideport/})).toBeVisible(),await j.click(N(n).getByRole(`button`,{name:/GitHub release/})),await A(t.getByText(`Metadata`)).toBeVisible(),await A(t.getByText(`Contents`)).toBeVisible(),await A(t.getByText(`Write access`)).toBeVisible(),await A(t.getByText(`None`)).toBeVisible(),await j.click(t.getByRole(`button`,{name:`Close app import`}));let r=t.getByRole(`heading`,{name:`Dice Roll`}).closest(`article`);if(!r)throw Error(`Dice Roll card was not rendered.`);let i=N(r).getByRole(`button`,{name:`Choose iPhone`});await j.click(i),await A(t.getByRole(`heading`,{name:`Choose the iPhone by name`})).toHaveFocus();let a=t.getByRole(`group`,{name:`Target iPhone`});await A(N(a).getByRole(`button`,{name:`Mara’s iPhone`})).toBeVisible(),await A(N(a).getByRole(`button`,{name:`Alex’s iPhone`})).toBeVisible(),await j.click(t.getByRole(`button`,{name:`Cancel iPhone choice`})),await A(i).toHaveFocus();let o=t.getByRole(`searchbox`,{name:`Search apps`});await j.type(o,`dice`),await A(t.getByRole(`heading`,{name:`Dice Roll`})).toBeVisible(),await A(t.queryByRole(`heading`,{name:`Cert Clock`})).not.toBeInTheDocument(),await j.clear(o)}},X={name:`09 Search · keyboard close and focus restoration`,args:{experience:`shell`,role:`owner`,initialRoute:`home`},play:async({canvasElement:e})=>{let t=N(e),n=t.getByRole(`button`,{name:`Search Sideport`});await j.keyboard(`{Control>}k{/Control}`);let r=t.getByRole(`dialog`,{name:`Search Sideport`}),i=N(r).getByRole(`searchbox`,{name:`Search Sideport`});await A(i).toHaveFocus(),await j.keyboard(`{Shift>}{Tab}{/Shift}`);let a=N(r).getAllByRole(`button`);await A(a[a.length-1]).toHaveFocus(),await j.tab(),await A(i).toHaveFocus(),await j.type(i,`Cert`),await A(N(r).getByRole(`button`,{name:/App Cert Clock Version/})).toBeVisible(),await j.keyboard(`{Escape}`),await M(()=>A(n).toHaveFocus())}},Z={name:`09b Activity · people, devices, apps, and attention`,args:{experience:`shell`,role:`owner`,initialRoute:`activity`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Activity`})).toBeVisible(),await A(t.getByText(`Sam’s iPhone needs the cable`)).toBeVisible(),await A(t.getByText(`Cert Clock updated on Mara’s iPhone`)).toBeVisible(),await A(t.getByText(`Alex’s iPhone came home`)).toBeVisible(),await A(t.getByText(`Sam joined Sideport`)).toBeVisible();let n=t.getByRole(`group`,{name:`Activity filters`});await j.click(N(n).getByRole(`button`,{name:`Apps`})),await A(t.getByText(`Dice Roll 0.1.1 became available`)).toBeVisible(),await A(t.queryByText(`Sam’s iPhone needs the cable`)).not.toBeInTheDocument()}},Q={name:`09c Devices · continuous USB monitor and three owners`,args:{experience:`shell`,role:`owner`,initialRoute:`devices`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByText(`USB port monitor is active`)).toBeVisible(),await A(t.getByRole(`heading`,{name:`Mara’s iPhone`})).toBeVisible(),await A(t.getByRole(`heading`,{name:`Alex’s iPhone`})).toBeVisible(),await A(t.getByRole(`heading`,{name:`Sam’s iPhone`})).toBeVisible(),await A(t.getByText(`Dice Roll update waiting`)).toBeVisible()}},je={name:`10 Apps · one Install action starts work`,args:{experience:`shell`,role:`member`,initialRoute:`apps`,memberName:`Mara`},play:async({canvasElement:e})=>{let t=N(e),n=t.getByRole(`heading`,{name:`Dice Roll`}).closest(`article`);if(!n)throw Error(`Dice Roll card was not rendered.`);await j.click(N(n).getByRole(`button`,{name:`Install`})),await A(t.getByRole(`heading`,{name:`Connect the iPhone to install Dice Roll`})).toBeVisible(),await A(t.queryByRole(`heading`,{name:`Choose an app`})).not.toBeInTheDocument(),await M(()=>A(t.getByRole(`heading`,{name:`Installing Dice Roll`})).toBeVisible(),{timeout:2500})}},Me={name:`10b Accessibility · keyboard app choice`,args:{experience:`add-iphone`,initialAssistantStep:`choose`,role:`member`,memberName:`Mara`},play:async({canvasElement:e})=>{let t=N(e),n=t.getAllByRole(`radio`);await A(n).toHaveLength(3),await j.tab(),await A(n[0]).toHaveFocus(),await j.keyboard(`{ArrowDown}`),await A(n[1]).toBeChecked(),await A(t.getByRole(`button`,{name:`Install Dice Roll`})).toBeVisible()}},Ne={name:`11 Install · verified and safe to unplug`,args:{experience:`add-iphone`,role:`member`,initialAssistantStep:`done`,memberName:`Mara`},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Installed — you can unplug`})).toBeVisible(),await A(t.getByText(`Completion chime would play`)).toBeVisible(),await A(t.getByText(`Best effort when the browser allows audio`)).toBeVisible(),await A(t.getByText(`Device verification represented`)).toBeVisible()}},Pe={name:`12 Mobile · member invitation at 390px`,args:{experience:`invitation`,role:`member`,memberName:`Mara`},parameters:{viewport:{defaultViewport:`sideportPhone390`,options:{sideportPhone390:{name:`Sideport phone 390px`,styles:{width:`390px`,height:`844px`},type:`mobile`}}}},play:async({canvasElement:e})=>{await A(N(e).getByRole(`heading`,{name:`Dragos invited you to Sideport`})).toBeVisible(),await A(e.scrollWidth).toBeLessThanOrEqual(e.clientWidth)}},Fe={name:`12b Mobile · complete one-cable journey at 390px`,args:{experience:`add-iphone`,initialAssistantStep:`connect`,role:`member`,memberName:`Mara`},parameters:{viewport:{defaultViewport:`sideportPhone390`,options:{sideportPhone390:{name:`Sideport phone 390px`,styles:{width:`390px`,height:`844px`},type:`mobile`}}}},play:async({canvasElement:e})=>{let t=N(e);await A(e.scrollWidth).toBeLessThanOrEqual(e.clientWidth),await j.click(t.getByRole(`button`,{name:`Start connecting`})),await M(()=>A(t.getByRole(`heading`,{name:`Turn on Developer Mode`})).toBeVisible(),{timeout:2500}),await A(e.scrollWidth).toBeLessThanOrEqual(e.clientWidth),await j.click(t.getByRole(`button`,{name:`I restarted and reconnected`})),await j.click(t.getByRole(`button`,{name:`Install Cert Clock`})),await M(()=>A(t.getByRole(`heading`,{name:`Installed — you can unplug`})).toBeVisible(),{timeout:3e3}),await A(e.scrollWidth).toBeLessThanOrEqual(e.clientWidth)}},$={name:`13 Accessibility · shell reflows at 320px`,args:{experience:`shell`,role:`member`,initialRoute:`apps`},parameters:{viewport:{defaultViewport:`sideportPhone320`,options:{sideportPhone320:{name:`Sideport phone 320px`,styles:{width:`320px`,height:`720px`},type:`mobile`}}}},play:async({canvasElement:e})=>{let t=N(e);await A(t.getByRole(`heading`,{name:`Apps`})).toBeVisible(),await A(e.scrollWidth).toBeLessThanOrEqual(e.clientWidth);let n=t.getByRole(`navigation`,{name:`Mobile Sideport navigation`});await A(N(n).getAllByRole(`button`)).toHaveLength(5),await A(N(n).getByRole(`button`,{name:`Apps`})).toHaveAttribute(`aria-current`,`page`)}},P.parameters={...P.parameters,docs:{...P.parameters?.docs,source:{originalSource:`{
  name: '01 Shell · owner home',
  args: {
    experience: 'shell',
    role: 'owner',
    initialRoute: 'home'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const shell = canvas.getByTestId('canonical-signed-in-shell');
    const navigation = within(shell).getByRole('navigation', {
      name: 'Sideport navigation'
    });
    const destinations = within(navigation).getAllByRole('button').map(button => button.textContent?.trim());
    await expect(destinations).toEqual(['Home', 'Apps', 'Devices', 'People', 'Activity', 'Settings']);
    await expect(within(navigation).queryByRole('button', {
      name: /Onboarding|Renewals|Operations|Diagnostics|Apple Access|Teams|Users|Install App/i
    })).not.toBeInTheDocument();
    await expect(canvas.getByRole('heading', {
      name: 'Apps and iPhones at a glance'
    })).toBeVisible();
    await expect(canvas.getByText('Cable ready for any trusted iPhone')).toBeVisible();
    await expect(canvas.getByText('1 update available')).toBeVisible();
    await expect(canvas.getByText(/Storybook fixture/)).toBeVisible();
  }
}`,...P.parameters?.docs?.source}}},F.parameters={...F.parameters,docs:{...F.parameters?.docs,source:{originalSource:`{
  name: '13 Settings · exact signing replacement impact',
  args: {
    experience: 'shell',
    role: 'owner',
    initialRoute: 'settings'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const page = within(canvasElement.ownerDocument.body);
    await userEvent.click(canvas.getByRole('button', {
      name: 'Review Apple signing'
    }));
    const dialog = page.getByRole('dialog', {
      name: 'Review Apple signing'
    });
    await expect(within(dialog).getByText(/one Apple account and one team/)).toBeVisible();
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Change account or team'
    }));
    await expect(within(dialog).getByLabelText('Password')).toHaveAttribute('autocomplete', 'current-password');
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Check current signing'
    }));
    await expect(within(dialog).getByText('Certificate ending A1B2 will be revoked')).toBeVisible();
    await expect(within(dialog).getByRole('button', {
      name: 'Replace signing identity'
    })).toBeDisabled();
    await userEvent.click(within(dialog).getByRole('checkbox'));
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Replace signing identity'
    }));
    await waitFor(() => expect(within(dialog).getByRole('heading', {
      name: 'New signing identity verified'
    })).toBeVisible(), {
      timeout: 2_000
    });
  }
}`,...F.parameters?.docs?.source}}},I.parameters={...I.parameters,docs:{...I.parameters?.docs,source:{originalSource:`{
  name: '02 Shell · member scope and safe projections',
  args: {
    experience: 'shell',
    role: 'member',
    initialRoute: 'home',
    memberName: 'Mara'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const navigation = canvas.getByRole('navigation', {
      name: 'Sideport navigation'
    });
    await expect(within(navigation).getAllByRole('button')).toHaveLength(6);
    await expect(canvas.queryByRole('button', {
      name: 'Add'
    })).not.toBeInTheDocument();
    await expect(canvas.queryByRole('button', {
      name: /Add another iPhone/
    })).not.toBeInTheDocument();
    await userEvent.click(within(navigation).getByRole('button', {
      name: 'Devices'
    }));
    await expect(canvas.queryByRole('button', {
      name: 'Add iPhone'
    })).not.toBeInTheDocument();
    await expect(canvas.getByText('Need another iPhone?')).toBeVisible();
    await userEvent.click(within(navigation).getByRole('button', {
      name: 'Activity'
    }));
    await expect(canvas.queryByRole('button', {
      name: /technical details/i
    })).not.toBeInTheDocument();
    await expect(canvas.queryByText(/network-usbmux|onboarding_v2|op_refresh_01/)).not.toBeInTheDocument();
    await userEvent.click(within(navigation).getByRole('button', {
      name: 'Settings'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Settings'
    })).toBeVisible();
    await expect(canvas.queryByRole('heading', {
      name: 'Signing'
    })).not.toBeInTheDocument();
  }
}`,...I.parameters?.docs?.source}}},Ae.parameters={...Ae.parameters,docs:{...Ae.parameters?.docs,source:{originalSource:`{
  name: '03 Setup · fresh deployment outside shell',
  args: {
    experience: 'first-run',
    role: 'owner'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Welcome to Sideport'
    })).toBeVisible();
    await expect(canvas.queryByRole('navigation', {
      name: 'Sideport navigation'
    })).not.toBeInTheDocument();
    await expect(canvas.getByText('Sideport is running')).toBeVisible();
    await expect(canvas.getByText(/Apple signing/)).toBeVisible();
    await expect(canvas.getByText(/First iPhone and app/)).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'How this installation is saved'
    }));
    await expect(canvas.getByText(/Docker keeps Sideport state/)).toBeVisible();
    await expect(canvas.getByText(/proposed Apple Container path is experimental/)).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Hide installation details'
    }));
  }
}`,...Ae.parameters?.docs?.source}}},L.parameters={...L.parameters,docs:{...L.parameters?.docs,source:{originalSource:`{
  name: '02b Setup · direct Owner passkey',
  args: {
    experience: 'owner-claim',
    ownerClaimState: 'setup',
    role: 'owner'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Finish setting up Sideport'
    })).toBeVisible();
    await expect(canvas.getByText(/no setup token or link to copy/)).toBeVisible();
    await expect(canvas.queryByLabelText(/API key|recovery key/i)).not.toBeInTheDocument();
    const create = canvas.getByRole('button', {
      name: 'Create passkey'
    });
    await expect(create).toBeDisabled();
    await userEvent.type(canvas.getByRole('textbox', {
      name: 'Name'
    }), 'Dragos');
    await userEvent.type(canvas.getByRole('textbox', {
      name: 'Email'
    }), 'dragos@example.test');
    await expect(create).toBeEnabled();
    await userEvent.click(create);
    await expect(canvas.getByRole('heading', {
      name: 'Welcome to Sideport'
    })).toBeVisible();
  }
}`,...L.parameters?.docs?.source}}},R.parameters={...R.parameters,docs:{...R.parameters?.docs,source:{originalSource:`{
  name: '02b.1 Setup · owner claim preview',
  args: {
    experience: 'owner-claim',
    ownerClaimState: 'setup',
    role: 'owner'
  }
}`,...R.parameters?.docs?.source}}},z.parameters={...z.parameters,docs:{...z.parameters?.docs,source:{originalSource:`{
  name: '02c Recovery · replace inaccessible owner',
  args: {
    experience: 'owner-claim',
    ownerClaimState: 'recovery',
    role: 'owner'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Recover Sideport owner access'
    })).toBeVisible();
    await expect(canvas.getByText('The current owner will be signed out.')).toBeVisible();
    await expect(canvas.getByText(/Apps, iPhones, signing state, and activity are retained/)).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Continue to sign in'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Recover owner access?'
    })).toBeVisible();
    await expect(canvas.getByText('Mara · mara@example.test')).toBeVisible();
    await expect(canvas.getByText(/Dragos will lose Owner access and be signed out/)).toBeVisible();
    await expect(canvas.getByText(/2 Members, 2 iPhones, 3 installed apps/)).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Recover owner access'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Apps and iPhones at a glance'
    })).toBeVisible();
  }
}`,...z.parameters?.docs?.source}}},B.parameters={...B.parameters,docs:{...B.parameters?.docs,source:{originalSource:`{
  name: '02c.1 Recovery · owner claim preview',
  args: {
    experience: 'owner-claim',
    ownerClaimState: 'recovery',
    role: 'owner'
  }
}`,...B.parameters?.docs?.source}}},V.parameters={...V.parameters,docs:{...V.parameters?.docs,source:{originalSource:`{
  name: '04 Setup · owner signing is not member login',
  args: {
    experience: 'first-run',
    role: 'owner'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByRole('button', {
      name: 'Connect Apple account'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Connect your Apple account'
    })).toBeVisible();
    await expect(canvas.getByText(/separate from member sign-in/)).toBeVisible();
    await expect(canvas.getByText(/Team selection is automatic/)).toBeVisible();
    await expect(canvas.getByText(/do not enter real credentials/)).toBeVisible();
    await expect(canvas.getByLabelText('Demo Apple Account email')).toHaveAttribute('autocomplete', 'off');
    await expect(canvas.getByLabelText('Demo password')).toHaveAttribute('autocomplete', 'off');
    await expect(canvas.getByRole('button', {
      name: 'Continue demo'
    })).toBeEnabled();
  }
}`,...V.parameters?.docs?.source}}},H.parameters={...H.parameters,docs:{...H.parameters?.docs,source:{originalSource:`{
  name: '05 People · invitation and signed-account consent',
  args: {
    experience: 'invitation',
    role: 'member',
    memberName: 'Mara'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Dragos invited you to Sideport'
    })).toBeVisible();
    await expect(canvas.getByText(/Face ID, Touch ID, Windows Hello/)).toBeVisible();
    await expect(canvas.getByText(/not official/)).toBeVisible();
    await expect(canvas.getByText(/No invitation, sign-in, account, device, app, or audio action occurs/)).toBeVisible();
    await expect(canvas.queryByLabelText(/password/i)).not.toBeInTheDocument();
    await expect(canvas.queryByRole('navigation', {
      name: 'Sideport navigation'
    })).not.toBeInTheDocument();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Continue to sign in'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Join Dragos’s Sideport?'
    })).toBeVisible();
    await expect(canvas.getByText('Mara · mara@example.test')).toBeVisible();
    await expect(canvas.getByRole('button', {
      name: 'Join Sideport'
    })).toBeVisible();
    await expect(canvas.getByText(/not kept in browser storage/)).toBeVisible();
  }
}`,...H.parameters?.docs?.source}}},U.parameters={...U.parameters,docs:{...U.parameters?.docs,source:{originalSource:`{
  name: '05b People · invitation expired',
  args: {
    experience: 'invitation',
    invitationState: 'expired',
    role: 'member'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Ask Dragos for a new link'
    })).toBeVisible();
    await expect(canvas.getByText('A new invitation is required.')).toBeVisible();
    await expect(canvas.queryByRole('button', {
      name: /passkey/i
    })).not.toBeInTheDocument();
  }
}`,...U.parameters?.docs?.source}}},W.parameters={...W.parameters,docs:{...W.parameters?.docs,source:{originalSource:`{
  name: '05c People · invitation already used',
  args: {
    experience: 'invitation',
    invitationState: 'used',
    role: 'member'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'This invitation has already been used'
    })).toBeVisible();
    await expect(canvas.getByRole('button', {
      name: 'Continue to sign in'
    })).toBeVisible();
    await expect(canvas.getByText(/confirms the signed-in account before showing membership/)).toBeVisible();
  }
}`,...W.parameters?.docs?.source}}},G.parameters={...G.parameters,docs:{...G.parameters?.docs,source:{originalSource:`{
  name: '05d People · access suspended',
  args: {
    experience: 'invitation',
    invitationState: 'suspended',
    role: 'member'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Your Sideport access is paused'
    })).toBeVisible();
    await expect(canvas.getByText('The owner must restore access.')).toBeVisible();
    await expect(canvas.queryByRole('button', {
      name: /passkey/i
    })).not.toBeInTheDocument();
  }
}`,...G.parameters?.docs?.source}}},K.parameters={...K.parameters,docs:{...K.parameters?.docs,source:{originalSource:`{
  name: '05e People · Authentik-owned recovery',
  args: {
    experience: 'invitation',
    invitationState: 'recovery',
    role: 'member'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Recover your Authentik sign-in'
    })).toBeVisible();
    await expect(canvas.getByRole('button', {
      name: 'Continue to sign-in recovery'
    })).toBeVisible();
    await expect(canvas.getByText(/Sideport cannot create, reset, or read your passkey/)).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Continue to sign-in recovery'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Your apps are ready'
    })).toBeVisible();
    await expect(canvas.queryByRole('heading', {
      name: 'Connect the iPhone'
    })).not.toBeInTheDocument();
  }
}`,...K.parameters?.docs?.source}}},q.parameters={...q.parameters,docs:{...q.parameters?.docs,source:{originalSource:`{
  name: '06 Member · invite to verified install',
  args: {
    experience: 'invitation',
    role: 'member',
    memberName: 'Mara'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await userEvent.click(canvas.getByRole('button', {
      name: 'Continue to sign in'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Join Dragos’s Sideport?'
    })).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Join Sideport'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Connect the iPhone'
    })).toBeVisible();
    await expect(canvas.getByText(/Tap “Trust” on the iPhone/)).toBeVisible();
    await expect(canvas.queryByRole('button', {
      name: /Pair|Add to Sideport/
    })).not.toBeInTheDocument();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Start connecting'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Unlock it and tap Trust'
    })).toBeVisible();
    await waitFor(() => expect(canvas.getByRole('heading', {
      name: 'Turn on Developer Mode'
    })).toBeVisible(), {
      timeout: 2500
    });
    await userEvent.click(canvas.getByRole('button', {
      name: 'I restarted and reconnected'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Choose an app'
    })).toBeVisible();
    await expect(canvas.getByText('Automatic refresh is on')).toBeVisible();
    await userEvent.click(canvas.getByRole('radio', {
      name: /Dice Roll/
    }));
    await userEvent.click(canvas.getByRole('button', {
      name: 'Install Dice Roll'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Installing Dice Roll'
    })).toBeVisible();
    await waitFor(() => expect(canvas.getByRole('heading', {
      name: 'Installed — you can unplug'
    })).toBeVisible(), {
      timeout: 3000
    });
    await expect(canvas.getByText(/Paired Wi-Fi attempted/)).toBeVisible();
    await expect(canvas.getByText(/Cable remains the reliable fallback/)).toBeVisible();
    await expect(canvas.getByText(/Sideport verifies installation, not successful launch/)).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Open Sideport'
    }));
    await expect(canvas.getByRole('navigation', {
      name: 'Sideport navigation'
    })).toBeVisible();
    await expect(canvas.getByRole('heading', {
      name: 'Your apps are ready'
    })).toBeVisible();
  }
}`,...q.parameters?.docs?.source}}},J.parameters={...J.parameters,docs:{...J.parameters?.docs,source:{originalSource:`{
  name: '07 Add · one shared iPhone assistant',
  args: {
    experience: 'shell',
    role: 'owner',
    initialRoute: 'activity'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const addTrigger = canvas.getByRole('button', {
      name: 'Add'
    });
    await userEvent.click(addTrigger);
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(addTrigger).toHaveFocus());
    await userEvent.click(addTrigger);
    await userEvent.click(canvas.getByRole('button', {
      name: /Add iPhone/
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Who will use this iPhone?'
    })).toBeVisible();
    await expect(canvas.queryByRole('heading', {
      name: 'Connect the iPhone'
    })).not.toBeInTheDocument();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Mara'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Connect the iPhone'
    })).toBeVisible();
    await expect(canvas.queryByRole('navigation', {
      name: 'Sideport navigation'
    })).not.toBeInTheDocument();
    await expect(canvas.getByText('Step 1 of 3')).toBeVisible();
  }
}`,...J.parameters?.docs?.source}}},Y.parameters={...Y.parameters,docs:{...Y.parameters?.docs,source:{originalSource:`{
  name: '08 Apps · approved library and sources',
  args: {
    experience: 'shell',
    role: 'owner',
    initialRoute: 'apps'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Apps'
    })).toBeVisible();
    await expect(canvas.getAllByRole('button', {
      name: 'Choose iPhone'
    })).toHaveLength(3);
    await expect(canvas.getByText(/Version 0.1.0 · On this Sideport/)).toBeVisible();
    await expect(canvas.getAllByText(/Version 0.1.0 · GitHub release/)).toHaveLength(2);
    await expect(canvas.getByRole('button', {
      name: /Import app/
    })).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: /Import app/
    }));
    const sourceGroup = canvas.getByRole('group', {
      name: 'IPA source'
    });
    await expect(within(sourceGroup).getByRole('button', {
      name: /This computer/
    })).toBeVisible();
    await expect(within(sourceGroup).getByRole('button', {
      name: /On this Sideport/
    })).toBeVisible();
    await userEvent.click(within(sourceGroup).getByRole('button', {
      name: /GitHub release/
    }));
    await expect(canvas.getByText('Metadata')).toBeVisible();
    await expect(canvas.getByText('Contents')).toBeVisible();
    await expect(canvas.getByText('Write access')).toBeVisible();
    await expect(canvas.getByText('None')).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Close app import'
    }));
    const diceCard = canvas.getByRole('heading', {
      name: 'Dice Roll'
    }).closest('article');
    if (!diceCard) throw new Error('Dice Roll card was not rendered.');
    const chooseTarget = within(diceCard).getByRole('button', {
      name: 'Choose iPhone'
    });
    await userEvent.click(chooseTarget);
    await expect(canvas.getByRole('heading', {
      name: 'Choose the iPhone by name'
    })).toHaveFocus();
    const targetGroup = canvas.getByRole('group', {
      name: 'Target iPhone'
    });
    await expect(within(targetGroup).getByRole('button', {
      name: 'Mara’s iPhone'
    })).toBeVisible();
    await expect(within(targetGroup).getByRole('button', {
      name: 'Alex’s iPhone'
    })).toBeVisible();
    await userEvent.click(canvas.getByRole('button', {
      name: 'Cancel iPhone choice'
    }));
    await expect(chooseTarget).toHaveFocus();
    const appSearch = canvas.getByRole('searchbox', {
      name: 'Search apps'
    });
    await userEvent.type(appSearch, 'dice');
    await expect(canvas.getByRole('heading', {
      name: 'Dice Roll'
    })).toBeVisible();
    await expect(canvas.queryByRole('heading', {
      name: 'Cert Clock'
    })).not.toBeInTheDocument();
    await userEvent.clear(appSearch);
  }
}`,...Y.parameters?.docs?.source}}},X.parameters={...X.parameters,docs:{...X.parameters?.docs,source:{originalSource:`{
  name: '09 Search · keyboard close and focus restoration',
  args: {
    experience: 'shell',
    role: 'owner',
    initialRoute: 'home'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const trigger = canvas.getByRole('button', {
      name: 'Search Sideport'
    });
    await userEvent.keyboard('{Control>}k{/Control}');
    const dialog = canvas.getByRole('dialog', {
      name: 'Search Sideport'
    });
    const searchbox = within(dialog).getByRole('searchbox', {
      name: 'Search Sideport'
    });
    await expect(searchbox).toHaveFocus();
    await userEvent.keyboard('{Shift>}{Tab}{/Shift}');
    const dialogButtons = within(dialog).getAllByRole('button');
    await expect(dialogButtons[dialogButtons.length - 1]).toHaveFocus();
    await userEvent.tab();
    await expect(searchbox).toHaveFocus();
    await userEvent.type(searchbox, 'Cert');
    await expect(within(dialog).getByRole('button', {
      name: /App Cert Clock Version/
    })).toBeVisible();
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(trigger).toHaveFocus());
  }
}`,...X.parameters?.docs?.source}}},Z.parameters={...Z.parameters,docs:{...Z.parameters?.docs,source:{originalSource:`{
  name: '09b Activity · people, devices, apps, and attention',
  args: {
    experience: 'shell',
    role: 'owner',
    initialRoute: 'activity'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Activity'
    })).toBeVisible();
    await expect(canvas.getByText('Sam’s iPhone needs the cable')).toBeVisible();
    await expect(canvas.getByText('Cert Clock updated on Mara’s iPhone')).toBeVisible();
    await expect(canvas.getByText('Alex’s iPhone came home')).toBeVisible();
    await expect(canvas.getByText('Sam joined Sideport')).toBeVisible();
    const filters = canvas.getByRole('group', {
      name: 'Activity filters'
    });
    await userEvent.click(within(filters).getByRole('button', {
      name: 'Apps'
    }));
    await expect(canvas.getByText('Dice Roll 0.1.1 became available')).toBeVisible();
    await expect(canvas.queryByText('Sam’s iPhone needs the cable')).not.toBeInTheDocument();
  }
}`,...Z.parameters?.docs?.source}}},Q.parameters={...Q.parameters,docs:{...Q.parameters?.docs,source:{originalSource:`{
  name: '09c Devices · continuous USB monitor and three owners',
  args: {
    experience: 'shell',
    role: 'owner',
    initialRoute: 'devices'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByText('USB port monitor is active')).toBeVisible();
    await expect(canvas.getByRole('heading', {
      name: 'Mara’s iPhone'
    })).toBeVisible();
    await expect(canvas.getByRole('heading', {
      name: 'Alex’s iPhone'
    })).toBeVisible();
    await expect(canvas.getByRole('heading', {
      name: 'Sam’s iPhone'
    })).toBeVisible();
    await expect(canvas.getByText('Dice Roll update waiting')).toBeVisible();
  }
}`,...Q.parameters?.docs?.source}}},je.parameters={...je.parameters,docs:{...je.parameters?.docs,source:{originalSource:`{
  name: '10 Apps · one Install action starts work',
  args: {
    experience: 'shell',
    role: 'member',
    initialRoute: 'apps',
    memberName: 'Mara'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const diceHeading = canvas.getByRole('heading', {
      name: 'Dice Roll'
    });
    const diceCard = diceHeading.closest('article');
    if (!diceCard) throw new Error('Dice Roll card was not rendered.');
    await userEvent.click(within(diceCard).getByRole('button', {
      name: 'Install'
    }));
    await expect(canvas.getByRole('heading', {
      name: 'Connect the iPhone to install Dice Roll'
    })).toBeVisible();
    await expect(canvas.queryByRole('heading', {
      name: 'Choose an app'
    })).not.toBeInTheDocument();
    await waitFor(() => expect(canvas.getByRole('heading', {
      name: 'Installing Dice Roll'
    })).toBeVisible(), {
      timeout: 2500
    });
  }
}`,...je.parameters?.docs?.source}}},Me.parameters={...Me.parameters,docs:{...Me.parameters?.docs,source:{originalSource:`{
  name: '10b Accessibility · keyboard app choice',
  args: {
    experience: 'add-iphone',
    initialAssistantStep: 'choose',
    role: 'member',
    memberName: 'Mara'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    const radios = canvas.getAllByRole('radio');
    await expect(radios).toHaveLength(3);
    await userEvent.tab();
    await expect(radios[0]).toHaveFocus();
    await userEvent.keyboard('{ArrowDown}');
    await expect(radios[1]).toBeChecked();
    await expect(canvas.getByRole('button', {
      name: 'Install Dice Roll'
    })).toBeVisible();
  }
}`,...Me.parameters?.docs?.source}}},Ne.parameters={...Ne.parameters,docs:{...Ne.parameters?.docs,source:{originalSource:`{
  name: '11 Install · verified and safe to unplug',
  args: {
    experience: 'add-iphone',
    role: 'member',
    initialAssistantStep: 'done',
    memberName: 'Mara'
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Installed — you can unplug'
    })).toBeVisible();
    await expect(canvas.getByText('Completion chime would play')).toBeVisible();
    await expect(canvas.getByText('Best effort when the browser allows audio')).toBeVisible();
    await expect(canvas.getByText('Device verification represented')).toBeVisible();
  }
}`,...Ne.parameters?.docs?.source}}},Pe.parameters={...Pe.parameters,docs:{...Pe.parameters?.docs,source:{originalSource:`{
  name: '12 Mobile · member invitation at 390px',
  args: {
    experience: 'invitation',
    role: 'member',
    memberName: 'Mara'
  },
  parameters: {
    viewport: {
      defaultViewport: 'sideportPhone390',
      options: {
        sideportPhone390: {
          name: 'Sideport phone 390px',
          styles: {
            width: '390px',
            height: '844px'
          },
          type: 'mobile'
        }
      }
    }
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Dragos invited you to Sideport'
    })).toBeVisible();
    await expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
  }
}`,...Pe.parameters?.docs?.source}}},Fe.parameters={...Fe.parameters,docs:{...Fe.parameters?.docs,source:{originalSource:`{
  name: '12b Mobile · complete one-cable journey at 390px',
  args: {
    experience: 'add-iphone',
    initialAssistantStep: 'connect',
    role: 'member',
    memberName: 'Mara'
  },
  parameters: {
    viewport: {
      defaultViewport: 'sideportPhone390',
      options: {
        sideportPhone390: {
          name: 'Sideport phone 390px',
          styles: {
            width: '390px',
            height: '844px'
          },
          type: 'mobile'
        }
      }
    }
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
    await userEvent.click(canvas.getByRole('button', {
      name: 'Start connecting'
    }));
    await waitFor(() => expect(canvas.getByRole('heading', {
      name: 'Turn on Developer Mode'
    })).toBeVisible(), {
      timeout: 2500
    });
    await expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
    await userEvent.click(canvas.getByRole('button', {
      name: 'I restarted and reconnected'
    }));
    await userEvent.click(canvas.getByRole('button', {
      name: 'Install Cert Clock'
    }));
    await waitFor(() => expect(canvas.getByRole('heading', {
      name: 'Installed — you can unplug'
    })).toBeVisible(), {
      timeout: 3000
    });
    await expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
  }
}`,...Fe.parameters?.docs?.source}}},$.parameters={...$.parameters,docs:{...$.parameters?.docs,source:{originalSource:`{
  name: '13 Accessibility · shell reflows at 320px',
  args: {
    experience: 'shell',
    role: 'member',
    initialRoute: 'apps'
  },
  parameters: {
    viewport: {
      defaultViewport: 'sideportPhone320',
      options: {
        sideportPhone320: {
          name: 'Sideport phone 320px',
          styles: {
            width: '320px',
            height: '720px'
          },
          type: 'mobile'
        }
      }
    }
  },
  play: async ({
    canvasElement
  }) => {
    const canvas = within(canvasElement);
    await expect(canvas.getByRole('heading', {
      name: 'Apps'
    })).toBeVisible();
    await expect(canvasElement.scrollWidth).toBeLessThanOrEqual(canvasElement.clientWidth);
    const mobileNavigation = canvas.getByRole('navigation', {
      name: 'Mobile Sideport navigation'
    });
    await expect(within(mobileNavigation).getAllByRole('button')).toHaveLength(5);
    await expect(within(mobileNavigation).getByRole('button', {
      name: 'Apps'
    })).toHaveAttribute('aria-current', 'page');
  }
}`,...$.parameters?.docs?.source}}},Ie=`OwnerHome.OwnerSigningReplacement.MemberHome.FreshDeployment.OwnerClaimSetup.OwnerClaimSetupPreview.OwnerClaimRecovery.OwnerClaimRecoveryPreview.OwnerAppleSigningIsSeparateFromMemberLogin.MemberInvitation.MemberInvitationExpired.MemberInvitationAlreadyUsed.MemberAccessSuspended.MemberPasskeyRecovery.FullMemberOneCableJourney.AddIPhoneFromSignedInShell.AppsLibraryAndSources.GlobalSearchKeyboardAndFocus.MultiUserActivity.ContinuousUsbMonitoring.OneTapInstallFromApps.KeyboardOnlyAppChoice.VerifiedSafeToUnplug.MobileMemberJourney390.MobileOneCableJourney390.MobileShell320Reflow`.split(`.`)}))();export{J as AddIPhoneFromSignedInShell,Y as AppsLibraryAndSources,Q as ContinuousUsbMonitoring,Ae as FreshDeployment,q as FullMemberOneCableJourney,X as GlobalSearchKeyboardAndFocus,Me as KeyboardOnlyAppChoice,G as MemberAccessSuspended,I as MemberHome,H as MemberInvitation,W as MemberInvitationAlreadyUsed,U as MemberInvitationExpired,K as MemberPasskeyRecovery,Pe as MobileMemberJourney390,Fe as MobileOneCableJourney390,$ as MobileShell320Reflow,Z as MultiUserActivity,je as OneTapInstallFromApps,V as OwnerAppleSigningIsSeparateFromMemberLogin,z as OwnerClaimRecovery,B as OwnerClaimRecoveryPreview,L as OwnerClaimSetup,R as OwnerClaimSetupPreview,P as OwnerHome,F as OwnerSigningReplacement,Ne as VerifiedSafeToUnplug,Ie as __namedExportsOrder,ke as default};