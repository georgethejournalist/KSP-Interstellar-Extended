﻿//namespace FNPlugin
//{
//	class ModableExperimentResultDialogPage : ExperimentResultDialogPage
//	{
//		Callback<ScienceData> onDiscardData;
//		Callback<ScienceData> onTransmitData;
//		Callback<ScienceData> onKeepData;

//		public ModableExperimentResultDialogPage(Part host, ScienceData experimentData, float xmitDataScalar, float labDataBoost, bool showTransmitWarning, string transmitWarningMessage, bool showResetOption, bool showLabOption, Callback<ScienceData> onDiscardData, Callback<ScienceData> onKeepData, Callback<ScienceData> onTransmitData, Callback<ScienceData> onSendToLab)
//			: base(host, experimentData, xmitDataScalar, labDataBoost, showTransmitWarning, transmitWarningMessage, showResetOption, new ScienceLabSearch(host.vessel, experimentData), onDiscardData, onKeepData, onTransmitData, onSendToLab)
//		{

//		}

//		public void setUpScienceData(string experiment_title, string experiment_results, float transmitValue, float recoveryValue, float data_size, float xmitScalar, float refValue)
//		{
//			this.title = experiment_title;
//			this.resultText = experiment_results;
//			//this.transmitValue = valueAfterTransmit;

//			// No need for manual tinkering now
//			//this.valueAfterTransmit = transmitValue;
//			//this.valueAfterRecovery = recoveryValue;
//			//this.dataSize = data_size;
//			//this.xmitDataScalar = xmitScalar;
//			//this.refValue = refValue;
//			//this.scienceValue = recoveryValue;
//			//this.baseTransmitValue = transmitValue;
//		}
//	}
//}
