﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace InterstellarFuelSwitch
{
    class ResourceStats
    {
        public PartResourceDefinition definition;
        //public double volume;
        public double maxAmount;
        public double currentAmount;
        public double amountRatio;
        public double retrieveAmount;
        public double transferRate = 1;
        public double normalizedDensity;
        public double conversionRatio = 1;
    }

    class InterstellarEquilibrium : InterstellarResourceConverter  { }

    class InterstellarResourceConverter : PartModule
    {
        // persistant control
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "Convert", guiUnits = "%"), UI_FloatRange()]
        public float convertPercentage = 0;

        // configs
        [KSPField]
        public bool showControlToggle = false;
        [KSPField]
        public string sliderText = string.Empty;
        [KSPField]
        public float percentageMaxValue = 100;
        [KSPField]
        public float percentageMinValue = -100;
        [KSPField]
        public float percentageStepIncrement = 5;
        [KSPField]
        public bool percentageSymetry = true;
        [KSPField]
        public string primaryResourceNames = string.Empty;
        [KSPField]
        public string secondaryResourceNames = string.Empty;
        [KSPField]
        public string primaryMolarMasses = string.Empty;
        [KSPField]
        public double primaryConversionEnergyCost = 500;
        [KSPField]
        public double secondaryConversionEnergyCost = 1000;

        [KSPField]
        bool retreivePrimary;
        [KSPField]
        bool retrieveSecondary;
        [KSPField]
        public double maxPowerPrimary = 10;
        [KSPField]
        public double maxPowerSecondary = 10;
        [KSPField]
        public bool requiresPrimaryLocalInEditor = true;
        [KSPField]
        public bool requiresPrimaryLocalInFlight = true;

        [KSPField]
        public bool primaryConversionCostPower = true;
        [KSPField]
        public bool secondaryConversionCostPower = true;

        //[KSPField(guiActive = true)]
        //public double receivedSourceAmount;
        //[KSPField(guiActive = true)]
        //public double requestedTargetAmount;
        //[KSPField(guiActive = true)]
        //public double receivedTargetAmount;

        //[KSPField(guiActive = true)]
        //public float primaryConversionRatio;
        //[KSPField(guiActive = true)]
        //public float secondaryConversionRatio;

        PartResourceDefinition definitionElectricCharge;
        BaseField convertPercentageField;
        List<ResourceStats> primaryResources;
        List<ResourceStats> secondaryResources;
        UI_FloatRange convertPecentageEditorFloatRange;
        UI_FloatRange convertPecentageFlightFloatRange;

        bool hasNullDefinitions = false;

        public override void OnStart(PartModule.StartState state)
        {
            definitionElectricCharge = PartResourceLibrary.Instance.GetDefinition("ElectricCharge");

            convertPercentageField = Fields["convertPercentage"];
            var floatrange = convertPercentageField.uiControlFlight as UI_FloatRange;

            primaryResources = primaryResourceNames.Split(';').Select(m => new ResourceStats() { definition = PartResourceLibrary.Instance.GetDefinition(m.Trim()) } ).ToList();
            secondaryResources = secondaryResourceNames.Split(';').Select(m => new ResourceStats() { definition = PartResourceLibrary.Instance.GetDefinition(m.Trim()) }).ToList();

            hasNullDefinitions = primaryResources.Any(m => m.definition == null) || secondaryResources.Any(m => m.definition == null);
            if (hasNullDefinitions)
            {
                convertPercentageField.guiActiveEditor = false;
                convertPercentageField.guiActive = false;
                return;
            }

            foreach (var resource in primaryResources)
            {
                //resource.volume = resource.definition.volume;

                if (resource.definition.density > 0 && resource.definition.volume > 0)
                    resource.normalizedDensity = resource.definition.density / resource.definition.volume;
            }

            foreach (var resource in secondaryResources)
            {
                //resource.volume = resource.definition.volume;

                if (resource.definition.density > 0 && resource.definition.volume > 0)
                    resource.normalizedDensity = resource.definition.density / resource.definition.volume;
            }

            if (primaryResources.Count == 1 && secondaryResources.Count == 1)
            {
                var primary = primaryResources.First();
                var secondary = secondaryResources.First();

                if (primary.normalizedDensity == 0 && secondary.normalizedDensity > 0)
                {
                    primary.normalizedDensity = secondary.normalizedDensity;
                }
                else if (secondary.normalizedDensity == 0 && primary.normalizedDensity > 0)
                {
                    secondary.normalizedDensity = primary.normalizedDensity;
                }

                //if (primary.volume == 0 && secondary.volume > 0)
                //    primary.volume = secondary.volume;
                //if (secondary.volume == 0 && primary.volume > 0)
                //    secondary.volume = primary.volume;

                //if (primary.volume == 0)
                //    primary.volume = 1;
                //if (secondary.volume == 0)
                //    secondary.volume = 1;

                if (primary.normalizedDensity > 0 && secondary.normalizedDensity > 0)
                {
                    secondary.conversionRatio = primary.normalizedDensity / secondary.normalizedDensity;
                    primary.conversionRatio = secondary.normalizedDensity / primary.normalizedDensity;
                }
                else if (primary.definition.unitCost > 0 && secondary.definition.unitCost > 0)
                {
                    secondary.conversionRatio = primary.definition.unitCost / secondary.definition.unitCost;
                    primary.conversionRatio = secondary.definition.unitCost / primary.definition.unitCost;
                }

                if (primary.conversionRatio == 0)
                    primary.conversionRatio = 1;
                if (secondary.conversionRatio == 0)
                    secondary.conversionRatio = 1;

                if (primary.normalizedDensity == 0)
                    primary.normalizedDensity = 0.001;
                if (secondary.normalizedDensity == 0)
                    secondary.normalizedDensity = 0.001;
            }
            
            // if slider text is missing, generate it
            if (string.IsNullOrEmpty(sliderText))
                convertPercentageField.guiName = String.Join("+", primaryResources.Select(m => m.definition.name).ToArray()) + "<->" + String.Join("+", secondaryResources.Select(m => m.definition.name).ToArray());
            else
                convertPercentageField.guiName = sliderText;

            convertPecentageFlightFloatRange = convertPercentageField.uiControlFlight as UI_FloatRange;
            convertPecentageFlightFloatRange.maxValue = percentageMaxValue;
            convertPecentageFlightFloatRange.minValue = percentageMinValue;
            convertPecentageFlightFloatRange.stepIncrement = percentageStepIncrement;
            convertPecentageFlightFloatRange.affectSymCounterparts = percentageSymetry ? UI_Scene.All : UI_Scene.None;

            convertPecentageEditorFloatRange = convertPercentageField.uiControlEditor as UI_FloatRange;
            convertPecentageEditorFloatRange.maxValue = percentageMaxValue;
            convertPecentageEditorFloatRange.minValue = percentageMinValue;
            convertPecentageEditorFloatRange.stepIncrement = percentageStepIncrement;
            convertPecentageEditorFloatRange.affectSymCounterparts = percentageSymetry ? UI_Scene.All : UI_Scene.None;
        }

        public void Update()
        {
            // exit if definition was not found
            if (hasNullDefinitions)
                return;

            // quit if any definition is missing
            if (!primaryResources.All(m => m.definition != null))
            {
                convertPercentageField.guiActive = false;
                return;
            }

            // in edit mode only show when primary resources are present
            if (requiresPrimaryLocalInEditor && HighLogic.LoadedSceneIsEditor)
            {
                convertPercentageField.guiActiveEditor = primaryResources.All(m => part.Resources.Contains( m.definition.id));
                return;
            }

            retreivePrimary = false;
            retrieveSecondary = false;

            // in flight mode, hide control if primary resources are not present 
            if (requiresPrimaryLocalInFlight && HighLogic.LoadedSceneIsFlight && !primaryResources.All(m => part.Resources.Contains(m.definition.id)))
            {
                 // hide interface and exit
                 convertPercentageField.guiActive = false;
                 return;
            }

            if (HighLogic.LoadedSceneIsEditor)
                return;

            foreach (var resource in primaryResources)
            {
                double currentAmount;
                double maxAmount;

                part.GetConnectedResourceTotals(resource.definition.id, out currentAmount, out maxAmount);

                if (maxAmount == 0)
                {
                    convertPercentageField.guiActive = false;
                    return;
                }

                resource.currentAmount = currentAmount;
                resource.maxAmount = maxAmount;
            }

            foreach (var resource in secondaryResources)
            {
                double currentAmount;
                double maxAmount;
                part.GetConnectedResourceTotals(resource.definition.id, out currentAmount, out maxAmount);

                if (maxAmount == 0)
                {
                    convertPercentageField.guiActive = false;
                    return;
                }

                resource.currentAmount = currentAmount;
                resource.maxAmount = maxAmount;
            }

            convertPercentageField.guiActive = true;

            if (convertPercentage == 0)
                return;

            primaryResources.ForEach(m => m.transferRate = maxPowerPrimary / primaryConversionEnergyCost / 1000 / m.normalizedDensity);
            secondaryResources.ForEach(m => m.transferRate = maxPowerSecondary / secondaryConversionEnergyCost / 1000 / m.normalizedDensity);

            primaryResources.ForEach(m => m.amountRatio = m.currentAmount / m.maxAmount);
            secondaryResources.ForEach(m => m.amountRatio = m.currentAmount / m.maxAmount);

            var percentageRatio = Math.Abs(convertPercentage) / 100d;
            var percentageRatioRemaining = 1 - percentageRatio;

            if (convertPercentage > 0 )
            {
                if (secondaryResources.Any(m => percentageRatio > m.amountRatio))
                {
                    retreivePrimary = true;
                    var neededAmount = secondaryResources.Min(m => Math.Max(percentageRatio - m.amountRatio, 0) * m.maxAmount / m.conversionRatio);
                    primaryResources.ForEach(m => m.retrieveAmount = neededAmount);
                }
                else if (percentageMinValue < 0)
                {
                    retrieveSecondary = true;
                    var availableSpaceInTarget = primaryResources.Min(m => (m.maxAmount - m.currentAmount) / m.conversionRatio);
                    secondaryResources.ForEach(m => m.retrieveAmount = Math.Min((Math.Max(m.amountRatio - percentageRatio, 0)) * m.maxAmount, availableSpaceInTarget));
                }
            }
            else if (convertPercentage < 0)
            {
                if (primaryResources.Any(m => percentageRatioRemaining < m.amountRatio))
                {
                    retrieveSecondary = true;
                    var neededAmount = primaryResources.Min(m => Math.Max(percentageRatio - m.amountRatio, 0) * m.maxAmount / m.conversionRatio);
                    secondaryResources.ForEach(m => m.retrieveAmount = neededAmount);
                }
                else if (percentageMaxValue > 0)
                {
                    retreivePrimary = true;
                    var availableSpaceInTarget = secondaryResources.Min(m => (m.maxAmount - m.currentAmount) / m.conversionRatio);
                    primaryResources.ForEach(m => m.retrieveAmount = Math.Min((Math.Max(m.amountRatio - percentageRatioRemaining, 0)) * m.maxAmount, availableSpaceInTarget));
                }
            }
        }

        public void FixedUpdate()
        {
            if (HighLogic.LoadedSceneIsEditor)
                return;

            //primaryConversionRatio = (float)primaryResources.First().conversionRatio;
            //secondaryConversionRatio = (float)secondaryResources.First().conversionRatio;

            if (retreivePrimary && primaryResources.Any(r => r.retrieveAmount > 0))
            {
                foreach (var resource in primaryResources)
                {
                    var fixedTransferRate = resource.transferRate * TimeWarp.fixedDeltaTime;
                    var transferRatio = resource.retrieveAmount >= fixedTransferRate ? 1 : resource.retrieveAmount / fixedTransferRate;

                    double powerRatio = 1;
                    if (primaryConversionCostPower)
                    {
                        var requestedPower = transferRatio * maxPowerPrimary * TimeWarp.fixedDeltaTime;
                        var receivedPower = part.RequestResource(definitionElectricCharge.id, requestedPower);
                        powerRatio = receivedPower / requestedPower;
                    }

                    var fixedRequest = Math.Min(fixedTransferRate, resource.retrieveAmount);
                    var receivedSourceAmount = part.RequestResource(resource.definition.id, fixedRequest * powerRatio);

                    double createdAmount = 0;
                    foreach(var secondary in secondaryResources)
                    {
                        var requestedTargetAmount = -receivedSourceAmount * secondary.conversionRatio;
                        var receivedTargetAmount = part.RequestResource(secondary.definition.id, requestedTargetAmount) / secondary.conversionRatio;
                        createdAmount += receivedTargetAmount;
                    }

                    var returned = part.RequestResource(resource.definition.id, createdAmount + receivedSourceAmount);
                    resource.retrieveAmount = resource.retrieveAmount - receivedSourceAmount - returned;
                }
            }
            else if (retrieveSecondary && secondaryResources.Any(r => r.retrieveAmount > 0))
            {
                foreach (var resource in secondaryResources)
                {
                    var fixedTransferRate = resource.transferRate * TimeWarp.fixedDeltaTime;
                    var transferRatio = resource.retrieveAmount >= fixedTransferRate ? 1 : resource.retrieveAmount / fixedTransferRate;

                    double powerRatio = 1;
                    if (secondaryConversionCostPower)
                    {
                        var requestedPower = transferRatio * maxPowerSecondary * TimeWarp.fixedDeltaTime;
                        var receivedPower = part.RequestResource(definitionElectricCharge.id, requestedPower);
                        powerRatio = receivedPower / requestedPower;
                    }

                    var fixedRequest = Math.Min(fixedTransferRate, resource.retrieveAmount);
                    var receivedSourceAmount = part.RequestResource(resource.definition.id, fixedRequest * powerRatio);

                    double createdAmount = 0;
                    foreach (var primary in primaryResources)
                    {
                        var requestedTargetAmount = -receivedSourceAmount * primary.conversionRatio;
                        var receivedTargetAmount = part.RequestResource(primary.definition.id, requestedTargetAmount) / primary.conversionRatio;
                        createdAmount += receivedTargetAmount;
                    }

                    var returned = part.RequestResource(resource.definition.id, createdAmount + receivedSourceAmount);
                    resource.retrieveAmount = resource.retrieveAmount - receivedSourceAmount - returned;
                }
            }
        }

        public override string GetInfo()
        {
            return "Primary: " + primaryResourceNames + "\n"
                 + "Secondary: " + secondaryResourceNames ;
        }
    }
}