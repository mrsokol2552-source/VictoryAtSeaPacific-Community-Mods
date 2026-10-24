using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using VaspCommunityMods;
using Path = System.IO.Path;

public sealed partial class RuntimeValidation
{
    private static Port FindPort(string name)
    {
        foreach(var navy in FactionManager.instance.navies) foreach(var port in navy.ports) if(port.name==name) return port;
        throw new Exception("Port not found: "+name);
    }
    private static Button IndustryButton(IndustryPanel panel,string name)
    {
        foreach(var button in panel.GetComponentsInChildren<Button>(true)) if(button.name==name) return button;
        throw new Exception("Industry button missing: "+name);
    }
    private static Port ProductionFixture(Port original)
    {
        var port=new Port(); port.name=original.name; port.homePort=true;
        port.supplyStore=new Supplies {fuelCapacity=100000,aviationFuelCapacity=100000,rationsCapacity=100000,mainAmmoCapacity=100000,torpedoCapacity=100000,depthChargeCapacity=100000,bombCapacity=100000,torpedoBombCapacity=100000,depthBombCapacity=100000};
        return port;
    }
    private static void Manufacture(Port port,int days)
    {
        var agent=new PortAIAgent(port); var method=AccessTools.Method(typeof(PortAIAgent),"UpdateManufacturing");
        for(int i=0;i<days;i++) method.Invoke(agent,new object[]{GameManager.instance.dayCycleInMinutes*60f});
    }
    private static void CheckProduction(Port port,int expectedUnits,string prefix)
    {
        var baseRates=Get<IDictionary<Supplies.Resource,int>>(GameManager.instance,"supplyGeneration"); var stock=port.supplyStore;
        Check(Math.Abs(stock.currentFuel-baseRates[Supplies.Resource.Fuel]*expectedUnits)<.05f,prefix+" fuel");
        Check(Math.Abs(stock.currentAviationFuel-baseRates[Supplies.Resource.AviationFuel]*expectedUnits)<.05f,prefix+" aviation fuel");
        Check(Math.Abs(stock.currentRations-baseRates[Supplies.Resource.Rations]*expectedUnits)<.05f,prefix+" rations");
        Check(stock.currentMainAmmo==baseRates[Supplies.Resource.Shell]*expectedUnits,prefix+" shells");
        Check(stock.currentTorpedoes==baseRates[Supplies.Resource.Torpedo]*expectedUnits,prefix+" torpedoes");
        Check(stock.currentDepthCharges==baseRates[Supplies.Resource.DepthCharge]*expectedUnits,prefix+" depth charges");
        Check(stock.currentBombs==baseRates[Supplies.Resource.Bomb]*expectedUnits,prefix+" bombs");
        Check(stock.currentTorpedoBombs==baseRates[Supplies.Resource.TorpedoBomb]*expectedUnits,prefix+" aerial torpedoes");
        Check(stock.currentDepthBombs==baseRates[Supplies.Resource.DepthBomb]*expectedUnits,prefix+" depth bombs");
    }
    private static byte[] FindManagerBlob(byte[] saved,string key)
    {
        var reader=new DataSerializer(saved); reader.GetInt(); byte[] ignored; reader.Get(out ignored);
        int n=reader.GetInt(); for(int i=0;i<n;i++){reader.GetInt();reader.GetString();}
        n=reader.GetInt(); for(int i=0;i<n;i++){reader.GetInt();reader.Get(out ignored);}
        n=reader.GetInt(); for(int i=0;i<n;i++){string name=reader.GetString();reader.Get(out ignored);if(name==key)return ignored;}
        return null;
    }
    private static bool SameBytes(byte[] a,byte[] b)
    {
        if(a==null || b==null || a.Length!=b.Length)return false;
        for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;
    }
    private IEnumerator TestIndustry()
    {
        Check(Plugin.Instance.IndustryAvailable && Plugin.Instance.IndustryEnabled && IndustryState.Instance,"industry module and native manager attached");
        var navy=FactionManager.instance.GetNavy(Player.instance.faction); var homes=navy.ports.FindAll(delegate(Port p){return p.homePort;});
        Record("INDUSTRY campaign="+HomeIndustry.CampaignActive+" player="+Player.instance.faction.name+" ports="+navy.ports.Count+" homes="+homes.Count);
        foreach(var p in navy.ports) Record("PORT "+p.name+" home="+p.homePort+" faction="+p.GetFaction().name);
        Check(homes.Count>=2,"campaign contains two home-port fixtures"); var home=homes[0];var second=homes[1];
        Check(HomeIndustry.GetLevel(home)==0 && HomeIndustry.GetLevel(second)==0,"legacy save starts at level zero without charges");
        float originalBonds=navy.warBonds; string reason;
        navy.warBonds=2999;Check(!HomeIndustry.TryPurchase(home,0,out reason) && navy.warBonds==2999 && HomeIndustry.GetLevel(home)==0,"insufficient 3000-bond purchase leaves state unchanged");
        navy.warBonds=10000;
        var ordinary=navy.ports.Find(delegate(Port p){return !p.homePort;});
        Check(!HomeIndustry.TryPurchase(ordinary,0,out reason) && navy.warBonds==10000,"ordinary ports cannot buy production");
        var enemy=FactionManager.instance.GetNavy(FactionManager.instance.GetFaction("UK")).ports.Find(delegate(Port p){return p.homePort;});
        Check(!HomeIndustry.TryPurchase(enemy,0,out reason) && navy.warBonds==10000,"foreign home port cannot spend player bonds");
        UIRoot.ShowScreen("BridgeScreen",false,true); yield return new WaitForSecondsRealtime(1);
        Set(UIBridgeScreen.instance,"selectedPort",home); yield return new WaitForSecondsRealtime(1);
        var panel=UIPortInformation.instance.GetComponent<IndustryPanel>(); Check(panel,"purchase controls attach to native port card");panel.Refresh();
        var buy=IndustryButton(panel,"Industry purchase");var cancel=IndustryButton(panel,"Industry cancel");
        Check(buy.gameObject.activeInHierarchy && buy.interactable,"owned home-port purchase button is visible and enabled");
        buy.onClick.Invoke();Check(navy.warBonds==10000 && HomeIndustry.GetLevel(home)==0 && cancel.gameObject.activeSelf,"first click requests confirmation without charge");
        cancel.onClick.Invoke();Check(navy.warBonds==10000 && HomeIndustry.GetLevel(home)==0,"cancel does not debit bonds");
        buy.onClick.Invoke();Set(UIBridgeScreen.instance,"selectedPort",second);panel.Refresh();
        Check(!cancel.gameObject.activeSelf && navy.warBonds==10000,"changing port cancels pending purchase");
        Set(UIBridgeScreen.instance,"selectedPort",home);panel.Refresh();buy.onClick.Invoke();buy.onClick.Invoke();
        Check(HomeIndustry.GetLevel(home)==1 && navy.warBonds==7000 && HomeIndustry.NextCost(home)==5000,"confirmed first purchase charges exactly 3000");
        Check(!HomeIndustry.TryPurchase(home,0,out reason) && navy.warBonds==7000,"stale double purchase is refused");
        var fraction=ProductionFixture(home);Manufacture(fraction,1);byte[] fractionalState=IndustryState.Instance.GetSerialized();
        IndustryState.Instance.OnClearGame();IndustryState.Instance.Unserialize(fractionalState);Manufacture(fraction,4);
        CheckProduction(fraction,6,"level-one fractional save then five days = six base days:");
        var production=ProductionFixture(home);Manufacture(production,25);CheckProduction(production,30,"level one, 25 native manufacturing ticks:");
        production=ProductionFixture(second);Manufacture(production,1);CheckProduction(production,1,"unupgraded home port:");
        Plugin.Instance.SetIndustryEnabled(false);production=ProductionFixture(home);Manufacture(production,1);CheckProduction(production,1,"disabled industry:");
        panel.Refresh();
        bool showsBase=false;foreach(var label in panel.GetComponentsInChildren<Text>(true))if(label.name=="Industry level" && label.text.Contains("выпуск 100%"))showsBase=true;
        Check(showsBase,"disabled industry card displays actual base production");
        Check(HomeIndustry.GetLevel(home)==1 && !HomeIndustry.TryPurchase(home,1,out reason) && navy.warBonds==7000,"disable preserves paid level and prevents purchases");Plugin.Instance.SetIndustryEnabled(true);
        navy.warBonds=4999;Check(!HomeIndustry.TryPurchase(home,1,out reason) && HomeIndustry.GetLevel(home)==1 && navy.warBonds==4999,"insufficient 5000-bond purchase leaves state unchanged");navy.warBonds=7000;
        panel.Refresh();buy.onClick.Invoke();Set(panel,"confirmUntil",Time.unscaledTime-1);yield return null;panel.Refresh();
        Check(!cancel.gameObject.activeSelf && navy.warBonds==7000,"expired confirmation cannot charge bonds");
        panel.Refresh();buy.onClick.Invoke();buy.onClick.Invoke();
        Check(HomeIndustry.GetLevel(home)==2 && navy.warBonds==2000 && HomeIndustry.ProductionPercent(home)==144,"confirmed second purchase charges 5000 and compounds to 144%");
        Check(!HomeIndustry.TryPurchase(home,2,out reason) && navy.warBonds==2000,"third level is refused without debit");
        production=ProductionFixture(home);Manufacture(production,25);CheckProduction(production,36,"level two, 25 native manufacturing ticks:");
        production=ProductionFixture(home);production.supplyStore.mainAmmoCapacity=10;production.supplyStore.currentMainAmmo=9;Manufacture(production,1);
        Check(production.supplyStore.currentMainAmmo==10 && production.supplyStore.mainAmmoCapacity==10,"native storage capacity still limits production");
        var transfer=new Supplies{fuelCapacity=10,mainAmmoCapacity=10};transfer.Add(new Supplies{currentFuel=1,currentMainAmmo=1});
        Check(transfer.currentFuel==1 && transfer.currentMainAmmo==1,"ordinary supplies transfer is never multiplied");
        byte[] good=IndustryState.Instance.GetSerialized();bool refused=false;
        try{IndustryState.Instance.Unserialize(new byte[]{1,2,3,4,5,6,7,8});}catch(FormatException){refused=true;}
        Check(refused && SameBytes(good,IndustryState.Instance.GetSerialized()),"invalid schema cannot partially overwrite loaded levels");
        string stateKey=typeof(IndustryState).ToString();byte[] priorBlob;bool hadBlob=SaveManager.instance.managerReferenceData.TryGetValue(stateKey,out priorBlob);
        byte[] future=new byte[]{86,65,83,80,73,78,68,57};SaveManager.instance.managerReferenceData[stateKey]=future;IndustryState.Instance.OnLoadGame();
        Check(IndustryState.Instance.LoadError!=null && SameBytes(future,IndustryState.Instance.GetSerialized()),"unknown manager schema is preserved byte-for-byte");
        Check(!HomeIndustry.TryPurchase(home,0,out reason) && navy.warBonds==2000,"unknown schema blocks spending");
        IndustryState.Instance.Unserialize(good);if(hadBlob)SaveManager.instance.managerReferenceData[stateKey]=priorBlob;else SaveManager.instance.managerReferenceData.Remove(stateKey);
        Check(HomeIndustry.GetLevel(home)==2 && IndustryState.Instance.LoadError==null,"valid state restores after unknown-schema test");
        navy.warBonds=10000;Check(HomeIndustry.TryPurchase(second,0,out reason) && HomeIndustry.GetLevel(home)==2 && HomeIndustry.GetLevel(second)==1 && navy.warBonds==7000,"ports purchase and retain independent levels");
        panel.Refresh();Application.CaptureScreenshot(Path.Combine(root,"industry-max.png"));yield return new WaitForSecondsRealtime(.5f);
        Set(UIBridgeScreen.instance,"selectedPort",second);panel.Refresh();buy.onClick.Invoke();Application.CaptureScreenshot(Path.Combine(root,"industry-confirm.png"));yield return new WaitForSecondsRealtime(.5f);cancel.onClick.Invoke();
        Set(UIBridgeScreen.instance,"selectedPort",ordinary);panel.Refresh();Check(!buy.gameObject.activeInHierarchy,"industry panel hides for ordinary ports");
        Set(UIBridgeScreen.instance,"selectedPort",null);panel.Refresh();Check(!buy.gameObject.activeInHierarchy,"industry panel hides when port is deselected");
        byte[] fullSave=SaveManager.instance.SaveCampaignData(ESaveType.Manual,"Private industry validation");
        byte[] blob=FindManagerBlob(fullSave,typeof(IndustryState).ToString());Check(blob!=null && SameBytes(blob,IndustryState.Instance.GetSerialized()),"real campaign serializer includes optional industry manager blob");
        File.WriteAllBytes(Path.Combine(root,"industry-state.bin"),blob);
        File.WriteAllBytes(Path.Combine(root,"industry-roundtrip.data"),fullSave);File.WriteAllLines(Path.Combine(root,"expected-ports.txt"),new[]{home.name,second.name});
        IndustryState.Instance.OnClearGame();Check(HomeIndustry.GetLevel(home)==0 && HomeIndustry.GetLevel(second)==0,"new/cleared campaign does not inherit levels");
        IndustryState.Instance.Unserialize(blob);Check(HomeIndustry.GetLevel(home)==2 && HomeIndustry.GetLevel(second)==1,"industry blob restores both ports");
        Check(File.Exists(Path.Combine(root,"industry-max.png")) && File.Exists(Path.Combine(root,"industry-confirm.png")),"industry card screenshots rendered");
        navy.warBonds=originalBonds;
    }
}
