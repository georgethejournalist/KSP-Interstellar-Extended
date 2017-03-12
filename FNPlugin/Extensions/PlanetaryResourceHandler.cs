using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FNPlugin.Extensions
{
    class PlanetaryResourceHandler
    {

        protected static Dictionary<int, List<PlanetaryResource>> body_Planetary_resource_list = new Dictionary<int, List<PlanetaryResource>>();

        public static double getPlanetaryResourceContent(int refBody, string resourcename)
        {
            List<PlanetaryResource> bodyPlanetaryComposition = GetPlanetaryCompositionForBody(refBody);
            PlanetaryResource resource = bodyPlanetaryComposition.FirstOrDefault(oor => oor.ResourceName == resourcename);
            return resource != null ? resource.ResourceAbundance : 0;
        }

        public static double getPlanetaryResourceContent(int refBody, int resource)
        {
            List<PlanetaryResource> bodyPlanetaryComposition = GetPlanetaryCompositionForBody(refBody);
            if (bodyPlanetaryComposition.Count > resource) return bodyPlanetaryComposition[resource].ResourceAbundance;
            return 0;
        }

        public static string getPlanetaryResourceName(int refBody, int resource)
        {
            List<PlanetaryResource> bodyPlanetaryComposition = GetPlanetaryCompositionForBody(refBody);
            if (bodyPlanetaryComposition.Count > resource)
            {
                return bodyPlanetaryComposition[resource].ResourceName;
            }
            return null;
        }

        public static string getPlanetaryResourceDisplayName(int refBody, int resource)
        {
            List<PlanetaryResource> bodyPlanetaryComposition = GetPlanetaryCompositionForBody(refBody);
            if (bodyPlanetaryComposition.Count > resource)
            {
                return bodyPlanetaryComposition[resource].DisplayName;
            }
            return null;
        }

        public static List<PlanetaryResource> GetPlanetaryCompositionForBody(CelestialBody celestialBody) // getter that uses celestial body as an argument
        {
            return GetPlanetaryCompositionForBody(celestialBody.flightGlobalsIndex); // calls the function that uses refBody int as an argument
        }

        public static List<PlanetaryResource> GetPlanetaryCompositionForBody(int refBody) // function for getting or creating Planetary composition
        {
            List<PlanetaryResource> bodyPlanetaryComposition = new List<PlanetaryResource>(); // create an object list for holding all the resources
            try
            {
                // check if there's a composition for this body
                if (body_Planetary_resource_list.ContainsKey(refBody))
                {
                    // skip all the other stuff and return the composition we already have
                    return body_Planetary_resource_list[refBody];
                }
                else
                {
                    CelestialBody celestialBody = FlightGlobals.Bodies[refBody]; // create a celestialBody object referencing the current body (makes it easier on us in the next lines)

                    // create composition from kspi Planetary definition file
                    bodyPlanetaryComposition = CreateFromKspiCrustDefinitionFile(refBody, celestialBody);

                    // add from stock resource definitions if missing
                    GenerateCompositionFromResourceAbundances(refBody, bodyPlanetaryComposition); // calls the generating function below

                    // if no planetary resource definition is created, create one based on celestialBody characteristics
                    if (bodyPlanetaryComposition.Sum(m => m.ResourceAbundance) < 0.5)
                        bodyPlanetaryComposition = GenerateCompositionFromCelestialBody(celestialBody);

                    // Add rare and isotopic resources
                    AddRaresAndIsotopesToCrustComposition(bodyPlanetaryComposition, celestialBody);

                    // add missing stock resources
                    AddMissingStockResources(refBody, bodyPlanetaryComposition);

                    // sort on resource abundance
                    bodyPlanetaryComposition = bodyPlanetaryComposition.OrderByDescending(bacd => bacd.ResourceAbundance).ToList();

                    // add to database for future reference
                    body_Planetary_resource_list.Add(refBody, bodyPlanetaryComposition);
                }
            }
            catch (Exception ex)
            {
                Debug.Log("[KSPI] - Exception while loading Planetary resources : " + ex.ToString());
            }
            return bodyPlanetaryComposition;
        }

        private static List<PlanetaryResource> CreateFromKspiCrustDefinitionFile(int refBody, CelestialBody celestialBody)
        {
            var bodyPlanetaryComposition = new List<PlanetaryResource>();

            ConfigNode Planetary_resource_pack = GameDatabase.Instance.GetConfigNodes("Planetary_RESOURCE_PACK_DEFINITION_KSPI").FirstOrDefault();

            Debug.Log("[KSPI] Loading Planetary data from pack: " + (Planetary_resource_pack.HasValue("name") ? Planetary_resource_pack.GetValue("name") : "unknown pack"));
            if (Planetary_resource_pack != null)
            {
                Debug.Log("[KSPI] - searching for ocean definition for " + celestialBody.name);
                List<ConfigNode> Planetary_resource_list = Planetary_resource_pack.nodes.Cast<ConfigNode>().Where(res => res.GetValue("celestialBodyName") == FlightGlobals.Bodies[refBody].name).ToList();
                if (Planetary_resource_list.Any())
                {
                    bodyPlanetaryComposition = Planetary_resource_list.Select(orsc => new PlanetaryResource(orsc.HasValue("resourceName") ? orsc.GetValue("resourceName") : null, double.Parse(orsc.GetValue("abundance")), orsc.GetValue("guiName"))).ToList();
                    //if (bodyPlanetaryComposition.Any())
                    //{
                    //    bodyPlanetaryComposition = bodyPlanetaryComposition.OrderByDescending(bacd => bacd.ResourceAbundance).ToList();
                    //    body_Planetary_resource_list.Add(refBody, bodyPlanetaryComposition);
                    //}
                }
            }
            return bodyPlanetaryComposition;
        }

        public static List<PlanetaryResource> GenerateCompositionFromCelestialBody(CelestialBody celestialBody) // generates Planetary composition based on planetary characteristics
        {
            List<PlanetaryResource> bodyPlanetaryComposition = new List<PlanetaryResource>(); // instantiate a new list that this function will be returning

            try
            {
                // return empty if there's no crust
                if (!celestialBody.hasSolidSurface)
                    return bodyPlanetaryComposition;
                

                // Lookup homeworld
                CelestialBody homeworld = FlightGlobals.Bodies.SingleOrDefault(b => b.isHomeWorld);

                double pressureAtSurface = celestialBody.GetPressure(0);

                if (celestialBody.Mass < homeworld.Mass * 10 && pressureAtSurface < 1000)
                {
                    if (pressureAtSurface > 200)
                    {
                        // it is Venus-like/Eve-like, use Eve as a template
                        bodyPlanetaryComposition = GetPlanetaryCompositionForBody(FlightGlobals.Bodies.Single(b => b.name == "Eve").flightGlobalsIndex);
                    }
                    else if (celestialBody.Mass > (homeworld.Mass / 2) && celestialBody.Mass < homeworld.Mass && pressureAtSurface < 100) // it's at least half as big as the homeworld and has significant atmosphere
                    {
                        // it is Laythe-like, use Laythe as a template
                        bodyPlanetaryComposition = GetPlanetaryCompositionForBody(FlightGlobals.Bodies.Single(b => b.name == "Laythe").flightGlobalsIndex);
                    }
                    else if (celestialBody.atmosphereContainsOxygen)
                    {
                        // it is Earth-like, use Earth as a template
                        bodyPlanetaryComposition = GetPlanetaryCompositionForBody(FlightGlobals.Bodies.Single(b => b.name == "Earth").flightGlobalsIndex);
                    }
                    else 
                    {
                        // it is a Mars-like, use Mars as template
                        bodyPlanetaryComposition = GetPlanetaryCompositionForBody(FlightGlobals.Bodies.Single(b => b.name == "Mars").flightGlobalsIndex);
                    }
                }

            }
            catch (Exception ex)
            {
                Debug.LogError("[KSPI] - Exception while generating Planetary resource composition from celestial properties : " + ex.ToString());
            }

            return bodyPlanetaryComposition;
        }

        public static List<PlanetaryResource> GenerateCompositionFromResourceAbundances(int refBody, List<PlanetaryResource> bodyPlanetaryComposition)
        {
            try
            {
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.Water, "LqdWater", "H2O", "Water", "Water");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.HeavyWater, "DeuteriumWater", "D2O", "HeavyWater", "HeavyWater");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.Water, "LqdNitrogen", "NitrogenGas", "Nitrogen", "Nitrogen");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.Oxygen, "LqdOxygen", "OxygenGas", "Oxygen", "Oxygen");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.CarbonDioxide, "LqdCO2", "CO2", "CarbonDioxide", "CarbonDioxide");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.CarbonMoxoxide, "LqdCO", "CO", "CarbonMonoxide", "CarbonMonoxide");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.Methane, "LqdMethane", "MethaneGas", "Methane", "Methane");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.Argon, "LqdArgon", "ArgonGas", "Argon", "Argon");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.LqdDeuterium, "LqdDeuterium", "DeuteriumGas", "Deuterium", "Deuterium");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.NeonGas, "LqdNeon", "NeonGas", "Neon", "Neon");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.XenonGas, "LqdXenon", "XenonGas", "Xenon", "Xenon");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.KryptonGas, "LqdKrypton", "KryptonGas", "Krypton", "Krypton");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.Sodium, "LqdSodium", "SodiumGas", "Sodium", "Sodium");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.UraniumNitride, "UraniumNitride", "UN", "UraniumNitride", "UraniumNitride");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.UraniumTetraflouride, "UraniumTetrafluoride", "UraniumTerraFloride", "UF4", "UF");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.Lithium6, "Lithium", "Lithium6", "Lithium-6", "LI");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.Lithium7, "Lithium7", "Lithium-7", "LI7", "Li7");
                AddResource(refBody, bodyPlanetaryComposition, InterstellarResourcesConfiguration.Instance.Plutonium238, "Plutionium", "Blutonium", "PU", "PU238");

                AddResource(InterstellarResourcesConfiguration.Instance.LqdHelium4, "Helium-4", refBody, bodyPlanetaryComposition, new[] { "LqdHe4", "Helium4Gas", "Helium4", "Helium-4", "He4Gas", "He4", "LqdHelium", "Helium", "HeliumGas" });
                AddResource(InterstellarResourcesConfiguration.Instance.LqdHelium3, "Helium-3", refBody, bodyPlanetaryComposition, new[] { "LqdHe3", "Helium3Gas", "Helium3", "Helium-3", "He3Gas", "He3" });
                AddResource(InterstellarResourcesConfiguration.Instance.Hydrogen, "Hydrogen", refBody, bodyPlanetaryComposition, new[] { "LqdHydrogen", "HydrogenGas", "Hydrogen", "H2", "Protium" });
            }
            catch (Exception ex)
            {
                Debug.LogError("[KSPI] - Exception while generating Planetary composition from defined abundances : " + ex.ToString());
            }

            return bodyPlanetaryComposition;
        }

        private static void AddMissingStockResources(int refBody, List<PlanetaryResource> bodyPlanetaryComposition)
        {
            // fetch all Planetary resources
            var allPlanetaryResources = ResourceMap.Instance.FetchAllResourceNames(HarvestTypes.Planetary);

            Debug.Log("[KSPI] - AddMissingStockResources : found " + allPlanetaryResources.Count + " resources");

            foreach (var resoureName in allPlanetaryResources)
            {
                // add resource if missing
                AddMissingResource(resoureName, refBody, bodyPlanetaryComposition);
            }
        }

        private static void AddMissingResource(string resourname, int refBody, List<PlanetaryResource> bodyPlanetaryComposition)
        {
            // verify it is a defined resource
            PartResourceDefinition definition = PartResourceLibrary.Instance.GetDefinition(resourname);
            if (definition == null)
            {
                Debug.LogWarning("[KSPI] - AddMissingResource : Failed to find resource definition for '" + resourname + "'");
                return;
            }

            // skip it already registred or used as a Synonym
            if (bodyPlanetaryComposition.Any(m => m.ResourceName == definition.name || m.DisplayName == definition.title || m.Synonyms.Contains(definition.name)))
            {
                Debug.Log("[KSPI] - AddMissingResource : Already found existing composition for '" + resourname + "'");
                return;
            }

            // retreive abundance
            var abundance = GetAbundance(definition.name, refBody);
            if (abundance <= 0)
            {
                Debug.LogWarning("[KSPI] - AddMissingResource : Abundance for resource '" + resourname + "' was " + abundance);
                return;
            }

            // create Planetaryresource from definition and abundance
            var PlanetaryResource = new PlanetaryResource(definition, abundance);

            // add to Planetary composition
            Debug.Log("[KSPI] - AddMissingResource : add resource '" + resourname + "'");
            bodyPlanetaryComposition.Add(PlanetaryResource);
        }

        private static void AddResource(string outputResourname, string displayname, int refBody, List<PlanetaryResource> bodyPlanetaryComposition, string[] variants)
        {
            var abundances = new[] { GetAbundance(outputResourname, refBody) }.Concat(variants.Select(m => GetAbundance(m, refBody)));

            var PlanetaryResource = new PlanetaryResource(outputResourname, abundances.Max(), displayname, variants);
            if (PlanetaryResource.ResourceAbundance > 0)
            {
                var existingResource = bodyPlanetaryComposition.FirstOrDefault(a => a.ResourceName == outputResourname);
                if (existingResource != null)
                {
                    Debug.Log("[KSPI] - replaced resource " + outputResourname + " with stock defined abundance " + PlanetaryResource.ResourceAbundance);
                    bodyPlanetaryComposition.Remove(existingResource);
                }
                bodyPlanetaryComposition.Add(PlanetaryResource);
            }
        }

        private static void AddResource(int refBody, List<PlanetaryResource> bodyPlanetaryComposition, string outputResourname, string inputResource1, string inputResource2, string inputResource3, string displayname)
        {
            var abundances = new[] { GetAbundance(inputResource1, refBody), GetAbundance(inputResource2, refBody), GetAbundance(inputResource2, refBody) };

            var PlanetaryResource = new PlanetaryResource(outputResourname, abundances.Max(), displayname, new[] { inputResource1, inputResource2, inputResource3 });
            if (PlanetaryResource.ResourceAbundance > 0)
            {
                var existingResource = bodyPlanetaryComposition.FirstOrDefault(a => a.ResourceName == outputResourname);
                if (existingResource != null)
                {
                    Debug.Log("[KSPI] - replaced resource " + outputResourname + " with stock defined abundance " + PlanetaryResource.ResourceAbundance);
                    bodyPlanetaryComposition.Remove(existingResource);
                }
                bodyPlanetaryComposition.Add(PlanetaryResource);
            }
        }

        private static void AddRaresAndIsotopesToCrustComposition(List<PlanetaryResource> bodyPlanetaryComposition, CelestialBody celestialBody)
        {
            // add heavywater based on water abundance in crust
            if (!bodyPlanetaryComposition.Any(m => m.ResourceName == InterstellarResourcesConfiguration.Instance.HeavyWater) && bodyPlanetaryComposition.Any(m => m.ResourceName == InterstellarResourcesConfiguration.Instance.Water))
            {
                var water = bodyPlanetaryComposition.FirstOrDefault(m => m.ResourceName == InterstellarResourcesConfiguration.Instance.Water);
                var heavywaterAbundance = water.ResourceAbundance / 6420;
                bodyPlanetaryComposition.Add(new PlanetaryResource(InterstellarResourcesConfiguration.Instance.HeavyWater, heavywaterAbundance, "HeavyWater", new[] { "HeavyWater", "D2O", "DeuteriumWater" }));
            }
        }

        private static float GetAbundance(string resourceName, int refBody)
        {
            return ResourceMap.Instance.GetAbundance(CreateRequest(resourceName, refBody));
        }

        public static AbundanceRequest CreateRequest(string resourceName, int refBody)
        {
            return new AbundanceRequest
            {
                ResourceType = HarvestTypes.Planetary,
                ResourceName = resourceName,
                BodyId = refBody,
                CheckForLock = false
            };
        }

    }
}
