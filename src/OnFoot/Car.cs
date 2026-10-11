using System;
using System.Collections.Generic;
using System.IO;
using Keystone;
using UnityEngine;

namespace OnFoot
{
    /// <summary>
    /// The ground crew's car: a small rover of the game's own parts (a RoveMate body on four RoveMax wheels, a seat
    /// for the driver, a storage crate on the back, two lamps), kept as a craft file in the mod's folder. From the
    /// space centre it is put on the runway with a kerbal in the seat, as the hangar's launch button would put a
    /// craft there, and is driven with the game's own rover keys. It is a craft like any other: it can be
    /// recovered, it carries what is put in its crate (by a kerbal on EVA), and a second kerbal can ride on it.
    /// </summary>
    public static class Car
    {
        public const string Site = "Runway";

        /// <summary>Who was put at the wheel the last time the car went out (nothing: it went out empty, nobody being free).</summary>
        public static string Driver;

        /// <summary>Where the craft is kept.</summary>
        public static string CraftPath => Path.Combine(KSPUtil.ApplicationRootPath, "GameData/OnFoot/PluginData/Crew Car.craft");

        /// <summary>Why the car cannot be put on the runway (or another launch site) just now, or nothing if it can.</summary>
        public static string WhyNot(string site = Site)
        {
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER) return "The crew car is sent out from the space centre's view.";
            if (HighLogic.CurrentGame == null) return "No game is loaded.";
            if (HighLogic.CurrentGame.Mode == Game.Modes.MISSION || HighLogic.CurrentGame.Mode == Game.Modes.SCENARIO_NON_RESUMABLE) return "Not in a mission or a scenario.";
            if (!File.Exists(CraftPath)) return "The car's craft file is missing (" + CraftPath + ").";
            ConfigNode craft = ConfigNode.Load(CraftPath);
            if (craft == null) return "The car's craft file could not be read.";
            string missing = "";
            if (!ShipConstruction.AllPartsFound(craft, ref missing)) return "The car needs parts that are not installed: " + missing;
            // (in a career or a science game the parts must have been researched, as for any craft)
            bool research = HighLogic.CurrentGame.Mode == Game.Modes.CAREER || HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX;
            float cost = 0f;
            foreach (ConfigNode part in craft.GetNodes("PART"))
            {
                AvailablePart ap = PartLoader.getPartInfoByName(KSPUtil.GetPartName(part.GetValue("part")));
                if (ap == null) return "The car has a part this game does not know.";
                if (research && ResearchAndDevelopment.Instance != null && !ResearchAndDevelopment.PartModelPurchased(ap)) return "Not researched yet: " + ap.title + ".";
                cost += ShipConstruction.GetPartCostsAndMass(part, ap, out float _, out float _, out float _, out float _);
            }
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER && Funding.Instance != null && Funding.Instance.Funds < cost)
                return "The car costs " + cost.ToString("N0") + " funds, and there are " + Funding.Instance.Funds.ToString("N0") + ".";
            List<ProtoVessel> there = ShipConstruction.FindVesselsLandedAt(HighLogic.CurrentGame.flightState, site);
            if (there != null && there.Count > 0) return "Something is on the " + (site == Site ? "runway" : site) + " already (" + there[0].vesselName + "): recover it first.";
            return null;
        }

        /// <summary>Put the car on the runway (or another launch site) with a kerbal in its seat, and go there.</summary>
        public static void Drive(string site = Site)
        {
            string why = WhyNot(site);
            if (why != null) throw new InvalidOperationException(why);
            ConfigNode craft = ConfigNode.Load(CraftPath);
            if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER && Funding.Instance != null)
            {
                float cost = 0f;
                foreach (ConfigNode part in craft.GetNodes("PART"))
                {
                    AvailablePart ap = PartLoader.getPartInfoByName(KSPUtil.GetPartName(part.GetValue("part")));
                    if (ap != null) cost += ShipConstruction.GetPartCostsAndMass(part, ap, out float _, out float _, out float _, out float _);
                }
                Funding.Instance.AddFunds(-cost, TransactionReasons.VesselRollout);
            }
            // (the game saved first, as it is before any launch)
            GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE);
            VesselCrewManifest crew = HighLogic.CurrentGame.CrewRoster.DefaultCrewForVessel(craft, null, true, false);
            // The game's own choice of crew never puts anyone in an outside seat (it leaves them for a kerbal to climb into): the
            // driver is put in it here, a pilot if one is free, else anyone free. With nobody free the car goes out empty, driven
            // by its probe core.
            Driver = null;
            foreach (PartCrewManifest part in crew.PartManifests)
            {
                if (part.PartInfo == null || part.PartInfo.partPrefab == null || part.PartInfo.partPrefab.FindModuleImplementing<KerbalSeat>() == null) continue;
                if (part.GetPartCrew().Length > 0 && part.GetPartCrew()[0] != null) { Driver = part.GetPartCrew()[0].name; break; }
                ProtoCrewMember chosen = null;
                foreach (ProtoCrewMember k in HighLogic.CurrentGame.CrewRoster.Crew)
                {
                    if (k.rosterStatus != ProtoCrewMember.RosterStatus.Available || crew.Contains(k)) continue;
                    if (chosen == null || (k.trait == KerbalRoster.pilotTrait && chosen.trait != KerbalRoster.pilotTrait)) chosen = k;
                }
                if (chosen != null) { part.AddCrewToSeat(chosen, 0); Driver = chosen.name; }
                break;
            }
            string flag = HighLogic.CurrentGame.flagURL;
            Kit.Log(Foot.Name, "the crew car goes out to the " + site);
            FlightDriver.StartWithNewLaunch(CraftPath, string.IsNullOrEmpty(flag) ? "Squad/Flags/default" : flag, site, crew);
        }
    }
}
