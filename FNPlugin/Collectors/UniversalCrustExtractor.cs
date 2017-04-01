using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using OpenResourceSystem;
using FNPlugin.Extensions;

namespace FNPlugin.Collectors
{
    class UniversalCrustExtractor : FNResourceSuppliableModule
    {
        // Persistent True
        [KSPField(isPersistant = true)]
        public bool bIsEnabled = false;
        [KSPField(isPersistant = true)]
        public double dLastActiveTime;
        [KSPField(isPersistant = true)]
        public double dLastPowerPercentage;
        [KSPField(isPersistant = true)]
        private double dLastConcentrationCrust = 0;
        [KSPField(isPersistant = true)]
        IDictionary<string, double> resourcePercentages = new Dictionary<string, double>(); // create a new persistent list for keeping track of percentages
        [KSPField(isPersistant = true)]
        List<CrustalResource> localResources;
        [KSPField(isPersistant = true)]
        int lastPlanetID = -1;


        // Part properties
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Drill size", guiUnits = " m\xB3")]
        public double drillSize = 0; // Volume of the collector's drill. Raise in part config (for larger drills) to make collecting faster.
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Drill effectiveness", guiFormat = "P1")]
        public double effectiveness = 1.0; // Effectiveness of the drill. Lower in part config (to a 0.5, for example) to slow down resource collecting.
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "MW Requirements", guiUnits = " MW")]
        public double mwRequirements = 1.0; // MW requirements of the drill. Affects heat produced.
        [KSPField(isPersistant = false, guiActiveEditor = true, guiName = "Waste Heat Modifier", guiFormat = "P1")]
        public double wasteHeatModifier = 0.25; // How much of the power requirements ends up as heat. Change in part cfg, treat as a percentage (1 = 100%). Higher modifier means more energy ends up as waste heat.

        // GUI
        [KSPField(guiActive = true, guiName = "Drill status")]
        protected string strCollectingStatus = "";
        [KSPField(guiActive = true, guiName = "Power Usage")]
        protected string strReceivedPower = "";
        [KSPField(guiActive = true, guiName = "Altitude", guiUnits = " m")]
        protected string strAltitude = "";



        [KSPEvent(guiActive = true, guiName = "Activate Drill", active = true)]
        public void ActivateCollector()
        {
            if (IsCollectLegal() == true) // will only be activated if the collecting of resource is legal
            {
                bTouchDown = TryRaycastToHitTerrain(); // check if there's ground within reach and if the drill is deployed
                if (bTouchDown == false) // if not, no collecting
                {
                    ScreenMessages.PostScreenMessage("Universal drill not in contact with ground. Make sure the drill is deployed and can reach the terrain.", 3.0f, ScreenMessageStyle.LOWER_CENTER);
                    DisableCollector();
                    return;
                }
                bIsEnabled = true;
                OnUpdate();
            }
        }

        [KSPEvent(guiActive = true, guiName = "Disable Drill", active = true)]
        public void DisableCollector()
        {
            bIsEnabled = false;
            OnUpdate();
        }

        [KSPAction("Activate Drill")]
        public void ActivateScoopAction(KSPActionParam param)
        {
            ActivateCollector();
        }

        [KSPAction("Disable Drill")]
        public void DisableScoopAction(KSPActionParam param)
        {
            DisableCollector();
        }

        [KSPAction("Toggle Drill")]
        public void ToggleScoopAction(KSPActionParam param)
        {
            if (bIsEnabled)
                DisableCollector();
            else
                ActivateCollector();
        }

        // internals
        private double dTotalWasteHeatProduction = 0; // total waste heat produced in the cycle
        private double dCrustalAmount = 0; // crust concentration at the current location
        private double localAbundance = 0;
        private int currentPlanetID;

        private bool bTouchDown = false; // helper bool, is the part touching the ground

        //protected Part part;
        //protected Vessel _vessel;
        protected String _status = "";

        private uint counter = 0; // helper counter for update cycles, so that we can only do some calculations once in a while
        private uint anotherCounter = 0; // helper counter for fixedupdate cycles, so that we can only do some calculations once in a while (I don't want to add complexity by using the previous counter in two places - also update and fixedupdate cycles can be out of sync, apparently)

        protected CelestialBody currentPlanet;
        protected CelestialBody localStar;

        private string strCrustAbundance = "";

        private AbundanceRequest resourceRequest = new AbundanceRequest // create a new request object that we'll reuse to get the current stock-system resource concentration
        {
            ResourceType = HarvestTypes.Planetary,
            ResourceName = "", // this will need to be updated before 'sending the request'
            BodyId = 1, // this will need to be updated before 'sending the request'
            Latitude = 0, // this will need to be updated before 'sending the request'
            Longitude = 0, // this will need to be updated before 'sending the request'
            Altitude = 0, // this will need to be updated before 'sending the request'
            CheckForLock = false
        };


        // end of internals

        public override void OnStart(PartModule.StartState state)
        {
            if (state == StartState.Editor) return; // collecting won't work in editor

            this.part.force_activate();

            localStar = GetCurrentStar();

            // this bit goes through parts that contain animations and disables the "Status" field in GUI part window so that it's less crowded
            List<ModuleAnimateGeneric> MAGlist = part.FindModulesImplementing<ModuleAnimateGeneric>();
            foreach (ModuleAnimateGeneric MAG in MAGlist)
            {
                MAG.Fields["status"].guiActive = false;
                MAG.Fields["status"].guiActiveEditor = false;
            }

            // verify collector was enabled 
            if (!bIsEnabled) return;

            // verify a timestamp is available
            if (dLastActiveTime == 0) return;

            // verify any power was available in previous state
            if (dLastPowerPercentage < 0.01) return;

            // verify vessel is landed, not splashed and not in atmosphere
            if (IsCollectLegal() == false) return;

            // calculate time difference since last time the vessel was active
            double dTimeDifference = (Planetarium.GetUniversalTime() - dLastActiveTime) * 55;

            // collect regolith for the amount of time that passed since last time (i.e. take care of offline collecting)
            MineResources(dTimeDifference, true);
        }


        public override void OnUpdate()
        {
            Events["ActivateCollector"].active = !bIsEnabled; // will activate the event (i.e. show the gui button) if the process is not enabled
            Events["DisableCollector"].active = bIsEnabled; // will show the button when the process IS enabled

            Fields["strReceivedPower"].guiActive = bIsEnabled;

            /* Crust concentration doesn't really need to be calculated and updated in gui on every update. 
             * By hiding it behind a counter that only runs this code once per hundred cycles, it should be more performance friendly.
            */
            if (++counter % 100 == 0) // increment counter then check if it is the hundreth update
            {
                dCrustalAmount = GetTotalCrustMinedPerTick(vessel.altitude, currentPlanet, drillSize, effectiveness);

                /* If collecting is legal, update the crust concentration in GUI, otherwise pass a zero string. 
                 * This way we shouldn't get readings when the vessel is flying or splashed or on a planet with an atmosphere.
                 */
                strCrustAbundance = IsCollectLegal() ? dCrustalAmount.ToString("P0") : "0"; // F1 string format means fixed point number with one decimal place (i.e. number 1234.567 would be formatted as 1234.5). I might change this eventually to P1 or P0 (num multiplied by hundred and percentage sign with 1 or 0 dec. places).
                // Also update the current altitude in GUI
                strAltitude = (vessel.altitude < 15000) ? (vessel.altitude).ToString("F0") : "Too damn high";
            }
        }

        public override void OnFixedUpdate()
        {
            if (FlightGlobals.fetch != null)
            {
                if (!bIsEnabled) // when it's not enabled
                {
                    strCollectingStatus = "Disabled";
                    return;
                }

                // won't collect in atmosphere, while splashed and while flying
                if (IsCollectLegal() == false)
                {
                    DisableCollector();
                    return;
                }

                // collect solar wind for a single frame
                MineResources(TimeWarp.fixedDeltaTime, false);

                // store current time in case vesel is unloaded
                dLastActiveTime = (float)Planetarium.GetUniversalTime();

                // store current solar wind concentration in case vessel is unloaded
                //dLastConcentrationCrust = CalculateCrustConcentration(FlightGlobals.currentMainBody.position, localStar.transform.position, vessel.altitude);
                dLastConcentrationCrust = GetTotalCrustMinedPerTick(vessel.altitude, currentPlanet, drillSize, effectiveness);

                /* This bit will check if the regolith drill has not lost contact with ground. Raycasts are apparently not all that expensive, but still, 
                 * the counter will delay the check so that it runs only once per hundred cycles. This should be enough and should make it more performance friendly and
                 * also less prone to kraken glitches. It also makes sure that this doesn't run before the vessel is fully loaded and shown to the player.
                 */
                if (++anotherCounter % 100 == 0)
                {
                    bTouchDown = TryRaycastToHitTerrain();
                    if (bTouchDown == false) // if not, disable collecting
                    {
                        ScreenMessages.PostScreenMessage("Universal drill not in contact with ground. Disabling drill.", 3.0f, ScreenMessageStyle.LOWER_CENTER);
                        DisableCollector();
                        return;
                    }
                }
            }
        }

        /** 
         * This function should allow this module to work in solar systems other than the vanilla KSP one as well. There are some instances where it will fail (systems with a black hole instead of a star etc).
         * It checks current reference body's temperature at 0 altitude. If it is less than 2k K, it checks this body's reference body next and so on.
         */
        protected CelestialBody GetCurrentStar()
        {
            int iDepth = 0;
            var star = FlightGlobals.currentMainBody;
            while ((iDepth < 10) && (star.GetTemperature(0) < 2000))
            {
                star = star.referenceBody;
                iDepth++;
            }
            if ((star.GetTemperature(0) < 2000) || (star.name == "Galactic Core"))
                star = null;

            return star;
        }

        // checks if the vessel is not in atmosphere and if it can therefore collect regolith. Also checks if the vessel is landed and if it is not splashed (not sure if non atmospheric bodies can have oceans in KSP or modded galaxies, let's put this in to be sure)
        private bool IsCollectLegal()
        {
            bool bCanCollect = false;


            if (vessel.checkLanded() == false || vessel.checkSplashed() == true)
            {
                strCrustAbundance = "0";
                return bCanCollect;
            }

            else if (FlightGlobals.currentMainBody.atmosphere == true) // won't collect in atmosphere
            {
                strCrustAbundance = "0";
                return bCanCollect;
            }
            else
            {
                bCanCollect = true;
                return bCanCollect; // all checks green, ok to collect
            }
        }

        // this snippet returns true if the part is extended
        private bool IsDrillExtended()
        {
            ModuleAnimationGroup thisPartsAnimGroup = this.part.FindModuleImplementing<ModuleAnimationGroup>();
            return thisPartsAnimGroup.isDeployed;
        }

        private bool TryRaycastToHitTerrain()
        {
            Vector3d partPosition = this.part.transform.position; // find the position of the transform in 3d space
            double scaleFactor = this.part.rescaleFactor; // what is the rescale factor of the drill?
            float drillDistance = (float)(5.0 * scaleFactor); // adjust the distance for the ray with the rescale factor, needs to be a float for raycast. The 5 is just about the reach of the drill.

            RaycastHit hit = new RaycastHit(); // create a variable that stores info about hit colliders etc.
            LayerMask terrainMask = 32768; // layermask in unity, number 1 bitshifted to the left 15 times (1 << 15), (terrain = 15, the bitshift is there so that the mask bits are raised; this is a good reading about that: http://answers.unity3d.com/questions/8715/how-do-i-use-layermasks.html)
            Ray drillPartRay = new Ray(partPosition, -part.transform.up); // this ray will start at the part's center and go down in local space coordinates (Vector3d.down is in world space)

            /* This little bit will fire a ray from the part, straight down, in the distance that the part should be able to reach.
             * It returns true if there is solid terrain in the reach AND the drill is extended. Otherwise false.
             * This is actually needed because stock KSP terrain detection is not really dependable. This module was formerly using just part.GroundContact 
             * to check for contact, but that seems to be bugged somehow, at least when paired with this drill - it works enough times to pass tests, but when testing 
             * this module in a difficult terrain, it just doesn't work properly. (I blame KSP planet meshes + Unity problems with accuracy further away from origin). 
            */
            Physics.Raycast(drillPartRay, out hit, drillDistance, terrainMask); // use the defined ray, pass info about a hit, go the proper distance and choose the proper layermask 
            if (hit.collider != null)
            {
                if (IsDrillExtended() == true)
                {
                    return true;
                }
            }
            return false;
        }

        private static double GetTotalCrustMinedPerTick(double altitude, CelestialBody planet, double drillSize, double effectiveness)
        {
            double crustalAmount = 0;
            // get the basic crustal thickness
            crustalAmount = CalculateCrustConcentration(altitude, planet);
            // adjust by the drillSize and effectiveness
            crustalAmount *= drillSize * effectiveness;

            return crustalAmount;
        }

        private double AdjustConcentrationForLocation(string resourceName)
        {
            double concentration = 0;
            // update the values of the stock abundance request with those for the current resource
            resourceRequest.ResourceName = resourceName;
            resourceRequest.BodyId = FlightGlobals.currentMainBody.flightGlobalsIndex;
            resourceRequest.Latitude = vessel.latitude;
            resourceRequest.Longitude = vessel.longitude;
            resourceRequest.Altitude = vessel.altitude;
            concentration = ResourceMap.Instance.GetAbundance(resourceRequest);
            return concentration;
        }

        // calculates concentration for the planet
        private static double CalculateCrustConcentration(double altitude, CelestialBody planet)
        {
            //double dAvgMunDistance = 13599840256;
            CelestialBody homeworld = FlightGlobals.Bodies.SingleOrDefault(b => b.isHomeWorld);
            double homeplanetMass = homeworld.Mass; // This will usually be Kerbin, but players can always use custom planet packs with a custom homeplanet or resized systems
            double planetMass = planet.Mass;

           /* I decided to incorporate an altitude modifier (similarly to regolith collector before).
            * According to various source, crust thickness is higher in higher altitudes (duh).
            * This is great from a gameplay perspective, because it makes an incentive for players to mine resources in more difficult circumstances 
            * (i.e. landing on highlands instead of flats etc.) and breaks the flatter-is-better base building strategy at least a bit.
            * This check will divide current altitude by 2500. At that arbitrarily-chosen altitude, we should be getting the basic concentration for the planet. 
            * Go to a higher terrain and you will find **more** resources. The + 500 shift is there so that even at altitude of 0 (i.e. Minmus flats etc.) there will
            * still be at least SOME resources to be mined, but not all that much.
            * This is pretty much the same as the regolith collector (which might get phased out eventually, I dunno).
            */
            double dAltModifier = (altitude + 500.0) / 2500.0;

            // if the dAltModifier is negative (if we're somehow trying to mine in a crack under sea level, perhaps), assign 0, otherwise keep it as it is
            dAltModifier = dAltModifier < 0 ? 0 : dAltModifier; 

            /* The actual concentration calculation is pretty simple. The more mass the current planet has in comparison to the homeworld, the more resources can be mined here.
             * While this might seem unfair to smaller moons and planets, this is actually somewhat realistic - bodies with smaller mass would be more porous,
             * so there might be lesser amount of heavier elements and less useful stuff to go around altogether.
             * This is then adjusted for the altitude modifier - there is simply more material to mine at high hills and mountains.
            */
            double dConcentration = dAltModifier * (planetMass / homeplanetMass); // get a basic concentration. The more mass the current planet has, the more crustal resources to be found here
            return dConcentration;
        }

        // the main collecting function
        private void MineResources(double deltaTimeInSeconds, bool offlineCollecting)
        {
            currentPlanet = FlightGlobals.currentMainBody;
            currentPlanetID = currentPlanet.flightGlobalsIndex;

            // get the power requirements (can be changed in the part cfg)
            double dPowerRequirementsMW = PluginHelper.PowerConsumptionMultiplier * mwRequirements;

            // calculate the provided power and consume it
            double dPowerReceivedMW = Math.Max(consumeFNResource(dPowerRequirementsMW * TimeWarp.fixedDeltaTime, FNResourceManager.FNRESOURCE_MEGAJOULES), 0);
            double dNormalisedRevievedPowerMW = dPowerReceivedMW / TimeWarp.fixedDeltaTime;

            // when the requirements are low enough, we can get the power needed from stock energy charge
            if (dPowerRequirementsMW < 5 && dNormalisedRevievedPowerMW <= dPowerRequirementsMW)
            {
                double dRequiredKW = (dPowerRequirementsMW - dNormalisedRevievedPowerMW) * 1000;
                double dReceivedKW = ORSHelper.fixedRequestResource(part, FNResourceManager.STOCK_RESOURCE_ELECTRICCHARGE, dRequiredKW * TimeWarp.fixedDeltaTime);
                dPowerReceivedMW += (dReceivedKW / 1000);
            }

            dLastPowerPercentage = offlineCollecting ? dLastPowerPercentage : (dPowerReceivedMW / dPowerRequirementsMW / TimeWarp.fixedDeltaTime);

            // if resolving offline collection, pass the saved value, because OnStart doesn't resolve the function CalculateCrustConcentration correctly
            if (offlineCollecting)
            {
                dCrustalAmount = dLastConcentrationCrust; 
            }
            else
            {
                // get the basic amount of crust mined
                dCrustalAmount = GetTotalCrustMinedPerTick(vessel.altitude, currentPlanet, drillSize, effectiveness);
            }

            // if we haven't been here before (will work the first time as well, because lastPlanetID is initialized to -1, but flightGlobalsIndexi start at 0
            if (currentPlanetID != lastPlanetID) 
            {
                // instantiate the persistent list
                localResources = new List<CrustalResource>();
                // get the resources that are here and add them to the list
                localResources = CrustalResourceHandler.GetCrustalCompositionForBody(currentPlanet);
            }

            foreach (CrustalResource resource in localResources)
            {
                string currentResourceName = resource.ResourceName;

                if (resource == null) // this resource doesn't interest us anymore
                    continue;

                var currentDefinition = PartResourceLibrary.Instance.GetDefinition(currentResourceName);

                if (currentDefinition == null) // this resource doesn't have definition, so it doesn't interest us anymore
                    continue;
                
                // calculate the spare room for this resource
                double currentResourceSpareRoom = part.GetConnectedResources(currentResourceName).Sum(r => r.maxAmount - r.amount) * currentDefinition.density;

                double currentResourcePercentage = 0;

                // if we haven't been here before, calculate the percentages
                if (currentPlanetID != lastPlanetID)
                {
                    currentResourcePercentage = CrustalResourceHandler.getCrustalResourceContent(currentPlanetID, currentResourceName);

                    // we'll store the value for next time
                    if (resourcePercentages.ContainsKey(currentResourceName)) // check if the dictionary is storing a value with this key already
                        resourcePercentages.Remove(currentResourceName); // then first do a remove

                    resourcePercentages.Add(currentResourceName, currentResourcePercentage); // add the current percentage to a dictionary, for easy access
                }
                else // we have been here already, so fetch the percentage from dictionary
                {
                    double percentage;
                    if (resourcePercentages.TryGetValue(currentResourceName, out percentage))
                        currentResourcePercentage = percentage;
                    else
                    {
                        Debug.Log("[KSPI] - Could not retrieve resource percentage from dictionary, setting to zero.");
                        currentResourcePercentage = 0;
                    }
                }

                // adjust the percentage for local abundance
                localAbundance = AdjustConcentrationForLocation(currentResourceName);

                // calculate the amount of the resource to collect
                double currentResourceRate = dCrustalAmount * deltaTimeInSeconds * currentResourcePercentage * localAbundance;

                // add the resource
                currentResourceRate = -part.RequestResource(currentResourceName, -currentResourceRate * deltaTimeInSeconds / currentDefinition.density, ResourceFlowMode.ALL_VESSEL) / deltaTimeInSeconds * currentDefinition.density;
            }

            if (offlineCollecting) // if collecting offline, let the player know the details
            {
                ScreenMessages.PostScreenMessage("The universal drill worked for " + deltaTimeInSeconds.ToString("F0") + " seconds, processing " + dCrustalAmount.ToString("F2") + " units of crustal rock in total.", 60.0f, ScreenMessageStyle.UPPER_CENTER);
            }

            // show in GUI
            strCollectingStatus = "Drilling the crust";

            if (!CheatOptions.InfiniteElectricity) // is this player using infinite electricity cheat mode?
            {
                // set the GUI string to state the number of KWs received if the MW requirements were lower than 5, otherwise in MW
                strReceivedPower = dPowerRequirementsMW < 5
                    ? (dLastPowerPercentage * dPowerRequirementsMW * 1000).ToString("0.0") + " KW / " + (dPowerRequirementsMW * 1000).ToString("0.0") + " KW"
                    : (dLastPowerPercentage * dPowerRequirementsMW).ToString("0.0") + " MW / " + dPowerRequirementsMW.ToString("0.0") + " MW";
            }
            else
            {
                strReceivedPower = "Inf.";
            }
                

            /* This takes care of wasteheat production (but takes into account if waste heat mechanics weren't disabled in the cheat menu).
             * It's affected by two properties of the drill part - its power requirements and waste heat production percentage.
             * More power hungry drills will produce more heat. More effective drills will produce less heat. More effective power hungry drills should produce
             * less heat than less effective power hungry drills. This should allow us to bring some variety into parts, if we want to.
             */

            if (!CheatOptions.IgnoreMaxTemperature) // is this player not using no-heat cheat mode?
            {
                dTotalWasteHeatProduction = dPowerRequirementsMW * wasteHeatModifier; // calculate amount of heat to be produced
                supplyFNResource(dTotalWasteHeatProduction * TimeWarp.fixedDeltaTime, FNResourceManager.FNRESOURCE_WASTEHEAT); // push the heat onto them
            }

            // finally, update the lastPlanetID
            if (lastPlanetID != currentPlanetID)
            {
                // update lastBodyID to this planets ID (i.e. remember this body)
                lastPlanetID = currentPlanetID;
            }
        }

    }
}
