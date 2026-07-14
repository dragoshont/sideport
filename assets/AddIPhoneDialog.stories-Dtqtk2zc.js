import{i as e}from"./preload-helper-xPQekRTU.js";import{a as t,i as n,n as r}from"./AddFlows--SkboTqx.js";var i,a,o,s,c,l,u,d,f,p,m,h,g,_,v,y,b,x;e((()=>{n(),t(),{expect:i,fn:a,userEvent:o,waitFor:s,within:c}=__STORYBOOK_MODULE_TEST__,l=a(e=>e),u={start:async()=>({operationId:`op-waiting-dragos`,status:`waiting`,stages:[{id:`wait-for-usb`,status:`waiting`,message:`Waiting for one iPhone over USB.`}]}),read:async()=>({operationId:`op-waiting-dragos`,status:`waiting`,stages:[{id:`wait-for-usb`,status:`waiting`,message:`Waiting for one iPhone over USB.`}]})},d={start:u.start,read:async()=>({operationId:`op-waiting-dragos`,status:`succeeded`,stages:[{id:`accept-device`,status:`succeeded`,message:`iPhone added.`}],result:{deviceEnrollment:{selectedDeviceUdid:`000081-dragos`,inventoryState:`accepted`}}})},f={start:u.start,read:u.read},p=a(async()=>({operationId:`should-not-run`,status:`waiting`,stages:[]})),m={start:async()=>({operationId:`op-access-revoked`,status:`recovery-required`,stages:[{id:`verify-lockdown`,status:`failed`,message:`Access changed.`}],error:{code:`operation-access-revoked`,message:`Your Sideport access changed while this operation was running.`}}),read:async()=>({operationId:`op-access-revoked`,status:`recovery-required`,stages:[],error:{code:`operation-access-revoked`,message:`Access changed.`}}),retry:p},h={title:`Sideport/Add iPhone Dialog`,component:r,args:{open:!0,onOpenChange:a(),onContinue:a(),demoMode:!1,canMutate:!0,memberName:`Dragos`,services:u,soundPlayer:l},parameters:{layout:`fullscreen`}},g={args:{attentionDelayMs:6e4},play:async({canvasElement:e})=>{l.mockClear();let t=c(e.ownerDocument.body).getByRole(`dialog`,{name:`Add an iPhone`});await o.click(c(t).getByRole(`button`,{name:`Connect iPhone`})),await i(c(t).getByText(`Waiting for Dragos’s iPhone…`)).toBeVisible(),await i(c(t).getByRole(`button`,{name:`Continue`})).toBeDisabled(),await i(l).toHaveBeenCalledWith(`listening`)}},_={args:{attentionDelayMs:20},play:async({canvasElement:e})=>{l.mockClear();let t=c(e.ownerDocument.body).getByRole(`dialog`,{name:`Add an iPhone`});await o.click(c(t).getByRole(`button`,{name:`Connect iPhone`})),await s(()=>i(c(t).getByText(/Still waiting/)).toBeVisible()),await i(c(t).getByText(/data-capable USB cable/)).toBeVisible(),await i(l).toHaveBeenCalledWith(`attention`)}},v={args:{services:d},play:async({canvasElement:e})=>{l.mockClear();let t=c(e.ownerDocument.body).getByRole(`dialog`,{name:`Add an iPhone`});await o.click(c(t).getByRole(`button`,{name:`Connect iPhone`})),await s(()=>i(c(t).getByText(`Dragos’s iPhone is ready`)).toBeVisible(),{timeout:3e3}),await i(c(t).getByRole(`button`,{name:`Continue`})).toBeEnabled(),await i(l).toHaveBeenCalledWith(`detected`)}},y={args:{services:f,resumeOperationId:`op-waiting-dragos`,attentionDelayMs:20},play:async({canvasElement:e})=>{l.mockClear();let t=c(e.ownerDocument.body).getByRole(`dialog`,{name:`Add an iPhone`});await i(await c(t).findByText(`Waiting for Dragos’s iPhone…`)).toBeVisible(),await s(()=>i(c(t).getByText(/Still waiting/)).toBeVisible()),await i(l).not.toHaveBeenCalled()}},b={args:{services:m},play:async({canvasElement:e})=>{p.mockClear();let t=c(e.ownerDocument.body).getByRole(`dialog`,{name:`Add an iPhone`});await o.click(c(t).getByRole(`button`,{name:`Connect iPhone`})),await i(await c(t).findByRole(`alert`)).toHaveTextContent(`access changed`),await i(p).not.toHaveBeenCalled()}},g.parameters={...g.parameters,docs:{...g.parameters?.docs,source:{originalSource:`{
  args: {
    attentionDelayMs: 60_000
  },
  play: async ({
    canvasElement
  }) => {
    soundCue.mockClear();
    const page = within(canvasElement.ownerDocument.body);
    const dialog = page.getByRole('dialog', {
      name: 'Add an iPhone'
    });
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Connect iPhone'
    }));
    await expect(within(dialog).getByText('Waiting for Dragos’s iPhone…')).toBeVisible();
    await expect(within(dialog).getByRole('button', {
      name: 'Continue'
    })).toBeDisabled();
    await expect(soundCue).toHaveBeenCalledWith('listening');
  }
}`,...g.parameters?.docs?.source}}},_.parameters={..._.parameters,docs:{..._.parameters?.docs,source:{originalSource:`{
  args: {
    attentionDelayMs: 20
  },
  play: async ({
    canvasElement
  }) => {
    soundCue.mockClear();
    const page = within(canvasElement.ownerDocument.body);
    const dialog = page.getByRole('dialog', {
      name: 'Add an iPhone'
    });
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Connect iPhone'
    }));
    await waitFor(() => expect(within(dialog).getByText(/Still waiting/)).toBeVisible());
    await expect(within(dialog).getByText(/data-capable USB cable/)).toBeVisible();
    await expect(soundCue).toHaveBeenCalledWith('attention');
  }
}`,..._.parameters?.docs?.source}}},v.parameters={...v.parameters,docs:{...v.parameters?.docs,source:{originalSource:`{
  args: {
    services: detectedServices
  },
  play: async ({
    canvasElement
  }) => {
    soundCue.mockClear();
    const page = within(canvasElement.ownerDocument.body);
    const dialog = page.getByRole('dialog', {
      name: 'Add an iPhone'
    });
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Connect iPhone'
    }));
    await waitFor(() => expect(within(dialog).getByText('Dragos’s iPhone is ready')).toBeVisible(), {
      timeout: 3_000
    });
    await expect(within(dialog).getByRole('button', {
      name: 'Continue'
    })).toBeEnabled();
    await expect(soundCue).toHaveBeenCalledWith('detected');
  }
}`,...v.parameters?.docs?.source}}},y.parameters={...y.parameters,docs:{...y.parameters?.docs,source:{originalSource:`{
  args: {
    services: resumedWaitingServices,
    resumeOperationId: 'op-waiting-dragos',
    attentionDelayMs: 20
  },
  play: async ({
    canvasElement
  }) => {
    soundCue.mockClear();
    const page = within(canvasElement.ownerDocument.body);
    const dialog = page.getByRole('dialog', {
      name: 'Add an iPhone'
    });
    await expect(await within(dialog).findByText('Waiting for Dragos’s iPhone…')).toBeVisible();
    await waitFor(() => expect(within(dialog).getByText(/Still waiting/)).toBeVisible());
    await expect(soundCue).not.toHaveBeenCalled();
  }
}`,...y.parameters?.docs?.source}}},b.parameters={...b.parameters,docs:{...b.parameters?.docs,source:{originalSource:`{
  args: {
    services: unrelatedRecoveryServices
  },
  play: async ({
    canvasElement
  }) => {
    unrelatedRecoveryRetry.mockClear();
    const page = within(canvasElement.ownerDocument.body);
    const dialog = page.getByRole('dialog', {
      name: 'Add an iPhone'
    });
    await userEvent.click(within(dialog).getByRole('button', {
      name: 'Connect iPhone'
    }));
    await expect(await within(dialog).findByRole('alert')).toHaveTextContent('access changed');
    await expect(unrelatedRecoveryRetry).not.toHaveBeenCalled();
  }
}`,...b.parameters?.docs?.source}}},x=[`WaitingForDragos`,`StillWaitingForDragos`,`DragosIPhoneDetected`,`ResumedWaitingDoesNotPlayAudio`,`AccessRevocationDoesNotAutoRetry`]}))();export{b as AccessRevocationDoesNotAutoRetry,v as DragosIPhoneDetected,y as ResumedWaitingDoesNotPlayAudio,_ as StillWaitingForDragos,g as WaitingForDragos,x as __namedExportsOrder,h as default};