﻿using LogDebugging;
using Outils;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

namespace ModuleProduction
{
    public static class CONST_PRODUCTION
    {
        public const String DOSSIER_PIECES = "Pieces";
        public const String DOSSIER_PIECES_APERCU = "Apercu";
        public const String DOSSIER_PIECES_CORPS = "Corps";
        public const String FICHIER_NOMENC = "Nomenclature.txt";
        public const String DOSSIER_LASERTOLE = "Laser tole";
        public const String DOSSIER_LASERTUBE = "Laser tube";
        public const String CAMPAGNE_DEPART_DECOMPTE = "CampagneDepartDecompte";
        public const String MAX_INDEXDIM = "MAX_INDEXDIM";
        public const String ID_PIECE = "ID_PIECE";
        public const String PIECE_ID_DOSSIERS = "PIECE_ID_DOSSIERS";
        public const String ID_CONFIG = "ID_CONFIG";
        public const String CONFIG_ID_DOSSIERS = "CONFIG_ID_DOSSIERS";
        public const String FILTRE_CORPS = "FILTRE_CORPS";
    }

    public static class OutilsProd
    {
        public static String pQuantite(this ModelDoc2 mdl)
        {
            CustomPropertyManager PM = mdl.Extension.get_CustomPropertyManager("");

            if (mdl.ePropExiste(CONSTANTES.PROPRIETE_QUANTITE))
                return Math.Max(mdl.eGetProp(CONSTANTES.PROPRIETE_QUANTITE).eToInteger(), 1).ToString();

            return "1";
        }

        public static Boolean pCreerConfigDepliee(this ModelDoc2 mdl, String NomConfigDepliee, String NomConfigPliee)
        {
            var cfg = mdl.ConfigurationManager.AddConfiguration(NomConfigDepliee, NomConfigDepliee, "", 0, NomConfigPliee, "");
            if (cfg.IsRef())
                return true;

            return false;
        }

        public static int pNbPli(this PartDoc piece)
        {
            int nb = 0;

            var liste = piece.eListeFonctionsDepliee();
            if (liste.Count == 0) return 0;

            Feature FonctionDepliee = liste[0];

            FonctionDepliee.eParcourirSousFonction(
                f =>
                {
                    if (f.GetTypeName2() == FeatureType.swTnUiBend)
                        nb++;

                    return false;
                }
                );

            return nb;
        }

        public static void pDeplierTole(this PartDoc piece, String nomConfigDepliee)
        {
            var mdl = piece.eModelDoc2();
            var liste = piece.eListeFonctionsDepliee();
            if (liste.Count == 0) return;

            Feature FonctionDepliee = liste[0];
            FonctionDepliee.eModifierEtat(swFeatureSuppressionAction_e.swUnSuppressFeature, nomConfigDepliee);
            FonctionDepliee.eModifierEtat(swFeatureSuppressionAction_e.swUnSuppressDependent, nomConfigDepliee);

            mdl.eEffacerSelection();

            FonctionDepliee.eParcourirSousFonction(
                f =>
                {
                    f.eModifierEtat(swFeatureSuppressionAction_e.swUnSuppressFeature, nomConfigDepliee);

                    if ((f.Name.ToLowerInvariant().StartsWith(CONSTANTES.LIGNES_DE_PLIAGE.ToLowerInvariant())) ||
                        (f.Name.ToLowerInvariant().StartsWith(CONSTANTES.CUBE_DE_VISUALISATION.ToLowerInvariant())))
                    {
                        f.eSelect(true);
                    }
                    return false;
                }
                );

            mdl.UnblankSketch();
            mdl.eEffacerSelection();

            piece.ePremierCorps(false).eVisible(true);
        }

        public static void pPlierTole(this PartDoc piece, String nomConfigPliee)
        {
            var mdl = piece.eModelDoc2();
            var liste = piece.eListeFonctionsDepliee();
            if (liste.Count == 0) return;

            Feature FonctionDepliee = liste[0];

            FonctionDepliee.eModifierEtat(swFeatureSuppressionAction_e.swSuppressFeature, nomConfigPliee);

            mdl.eEffacerSelection();

            FonctionDepliee.eParcourirSousFonction(
                f =>
                {
                    if ((f.Name.ToLowerInvariant().StartsWith(CONSTANTES.LIGNES_DE_PLIAGE.ToLowerInvariant())) ||
                        (f.Name.ToLowerInvariant().StartsWith(CONSTANTES.CUBE_DE_VISUALISATION.ToLowerInvariant())))
                    {
                        f.eSelect(true);
                    }
                    return false;
                }
                );

            mdl.BlankSketch();
            mdl.eEffacerSelection();

            piece.ePremierCorps(false).eVisible(true);
        }

        public static void pMasquerEsquisses(this ModelDoc2 mdl)
        {
            mdl.eParcourirFonctions(
                                    f =>
                                    {
                                        if (f.GetTypeName2() == FeatureType.swTnFlatPattern)
                                            return true;
                                        else if (f.GetTypeName2() == FeatureType.swTnProfileFeature)
                                        {
                                            f.eSelect(false);
                                            mdl.BlankSketch();
                                            mdl.eEffacerSelection();
                                        }
                                        return false;
                                    },
                                    true
                                    );
        }

        public static void pCreerDvp(this Corps corps, String dossierPiece, Boolean _supprimerLesAnciennesConfigs = false)
        {
            try
            {
                if (corps.TypeCorps != eTypeCorps.Tole) return;

                String Repere = CONSTANTES.PREFIXE_REF_DOSSIER + corps.Repere;

                var nomFichier = Repere + OutilsProd.pExtPiece;
                var chemin = Path.Combine(dossierPiece, nomFichier);
                if (!File.Exists(chemin)) return;

                var mdl = Sw.eOuvrir(chemin);
                if (mdl.IsNull()) return;

                var NomCfgPliee = mdl.eNomConfigActive();
                var Piece = mdl.ePartDoc();
                var Tole = Piece.ePremierCorps();

                if (_supprimerLesAnciennesConfigs)
                    mdl.pSupprimerLesAnciennesConfigs();

                mdl.pMasquerEsquisses();

                if (!mdl.Extension.LinkedDisplayState)
                {
                    mdl.Extension.LinkedDisplayState = true;

                    foreach (var c in mdl.eListeConfigs(eTypeConfig.Tous))
                        c.eRenommerEtatAffichage();
                }

                mdl.EditRebuild3();

                String NomConfigDepliee = Sw.eNomConfigDepliee(NomCfgPliee, Repere);

                if (!mdl.pCreerConfigDepliee(NomConfigDepliee, NomCfgPliee))
                {
                    WindowLog.Ecrire("       - Config non crée");
                    return;
                }
                try
                {
                    mdl.ShowConfiguration2(NomConfigDepliee);
                    mdl.pMasquerEsquisses();
                    mdl.EditRebuild3();
                    Piece.pDeplierTole(NomConfigDepliee);

                    mdl.ShowConfiguration2(NomCfgPliee);
                    mdl.pMasquerEsquisses();
                    mdl.EditRebuild3();
                    Piece.pPlierTole(NomCfgPliee);
                    WindowLog.EcrireF("  - Dvp crée : {0}", NomConfigDepliee);
                }
                catch (Exception e)
                {
                    WindowLog.Ecrire("  - Erreur de dvp");
                    Log.Message(new Object[] { e });
                }

                mdl.ShowConfiguration2(NomCfgPliee);
                mdl.EditRebuild3();
                mdl.eSauver();
            }
            catch (Exception e)
            {
                Log.Message(new Object[] { e });
            }
        }

        public static void pSupprimerLesAnciennesConfigs(this ModelDoc2 mdl)
        {
            if (mdl.eNomConfigActive().eEstConfigDepliee())
                mdl.ShowConfiguration2(mdl.eListeNomConfiguration()[0]);

            mdl.EditRebuild3();

            WindowLog.Ecrire("  - Suppression des cfgs depliées :");
            var liste = mdl.eListeConfigs(eTypeConfig.Depliee);
            if (liste.Count == 0)
                WindowLog.EcrireF("   Aucune configuration à supprimer");

            foreach (Configuration Cf in liste)
            {
                String IsSup = Cf.eSupprimerConfigAvecEtatAff(mdl) ? "Ok" : "Erreur";
                WindowLog.EcrireF("  {0} : {1}", Cf.Name, IsSup);
            }
        }

        /// <summary>
        /// Renvoi la liste unique des modeles et configurations
        /// Modele : ModelDoc2
        ///                   |-Config1 : Nom de la configuration
        ///                   |     |-Nb : quantite de configuration identique dans le modele complet
        ///                   |-Config 2
        ///                   | etc...
        /// </summary>
        /// <param name="mdlBase"></param>
        /// <param name="composantsExterne"></param>
        /// <param name="filtreTypeCorps"></param>
        /// <returns></returns>
        public static SortedDictionary<ModelDoc2, SortedDictionary<String, int>> pListerComposants(this ModelDoc2 mdlBase, Boolean composantsExterne = false)
        {
            SortedDictionary<ModelDoc2, SortedDictionary<String, int>> dic = new SortedDictionary<ModelDoc2, SortedDictionary<String, int>>(new CompareModelDoc2());

            try
            {
                Action<Component2> Test = delegate (Component2 comp)
                {
                    var cfg = comp.eNomConfiguration();

                    if (comp.IsSuppressed() || comp.ExcludeFromBOM || !cfg.eEstConfigPliee() || comp.TypeDoc() != eTypeDoc.Piece) return;

                    var mdl = comp.eModelDoc2();

                    if (dic.ContainsKey(mdl))
                        if (dic[mdl].ContainsKey(cfg))
                        {
                            dic[mdl][cfg] += 1;
                            return;
                        }

                    if (dic.ContainsKey(mdl))
                        dic[mdl].Add(cfg, 1);
                    else
                    {
                        var lcfg = new SortedDictionary<String, int>(new WindowsStringComparer());
                        lcfg.Add(cfg, 1);
                        dic.Add(mdl, lcfg);
                    }
                };

                if (mdlBase.TypeDoc() == eTypeDoc.Piece)
                    Test(mdlBase.eComposantRacine());
                else
                {
                    mdlBase.eComposantRacine().eRecParcourirComposantBase(
                        Test,
                        // On ne parcourt pas les assemblages exclus
                        c =>
                        {
                            if (c.ExcludeFromBOM)
                                return false;

                            return true;
                        }
                        );
                }
            }
            catch (Exception e) { Log.LogErreur(new Object[] { e }); }

            return dic;
        }

        public static String pCreerDossier(this ModelDoc2 mdl, String dossier)
        {
            var chemin = Path.Combine(mdl.eDossier(), dossier);
            if (!Directory.Exists(chemin))
                Directory.CreateDirectory(chemin);

            return chemin;
        }

        public static String pCreerFichierTexte(this ModelDoc2 mdl, String dossier, String fichier)
        {
            var chemin = Path.Combine(mdl.eDossier(), dossier, fichier);
            if (!File.Exists(chemin))
                File.WriteAllText(chemin, "", Encoding.GetEncoding(1252));

            return chemin;
        }

        public static int pRechercherIndiceDossier(this ModelDoc2 mdl, String dossier)
        {
            int indice = 0;
            String chemin = Path.Combine(mdl.eDossier(), dossier);

            if (Directory.Exists(chemin))
                foreach (var d in Directory.EnumerateDirectories(chemin, "*", SearchOption.TopDirectoryOnly))
                    indice = Math.Max(indice, new DirectoryInfo(d).Name.eToInteger());

            return indice;
        }

        public static String pDossierPiece(this ModelDoc2 mdl)
        {
            return Path.Combine(mdl.eDossier(), CONST_PRODUCTION.DOSSIER_PIECES);
        }

        public static String pFichierNomenclature(this ModelDoc2 mdl)
        {
            return Path.Combine(mdl.eDossier(), CONST_PRODUCTION.DOSSIER_PIECES, CONST_PRODUCTION.FICHIER_NOMENC);
        }

        public static String pDossierLaserTole(this ModelDoc2 mdl)
        {
            return Path.Combine(mdl.eDossier(), CONST_PRODUCTION.DOSSIER_LASERTOLE);
        }

        public static String pDossierLaserTube(this ModelDoc2 mdl)
        {
            return Path.Combine(mdl.eDossier(), CONST_PRODUCTION.DOSSIER_LASERTUBE);
        }

        public static ListeSortedCorps pChargerNomenclature(this ModelDoc2 mdl, eTypeCorps type = eTypeCorps.Tous)
        {
            var Liste = new ListeSortedCorps();

            var chemin = mdl.pFichierNomenclature();

            if (File.Exists(chemin))
            {
                using (var sr = new StreamReader(chemin, Encoding.GetEncoding(1252)))
                {
                    // On lit la première ligne contenant l'entête des colonnes
                    String ligne = sr.ReadLine();

                    if (ligne.IsRef())
                    {
                        // On récupère la campagne de départ
                        if (ligne.StartsWith(CONST_PRODUCTION.CAMPAGNE_DEPART_DECOMPTE))
                        {
                            var tab = ligne.Split(new char[] { '\t' });
                            Liste.CampagneDepartDecompte = tab[1].eToInteger();
                            ligne = sr.ReadLine();
                        }

                        while ((ligne = sr.ReadLine()) != null)
                        {
                            if (!String.IsNullOrWhiteSpace(ligne))
                            {
                                var c = new Corps(ligne, mdl);
                                if (type.HasFlag(c.TypeCorps))
                                    Liste.Add(c.Repere, c);
                            }
                        }
                    }
                }
            }

            return Liste;
        }

        public static int pIndiceMaxNomenclature(this ModelDoc2 mdl)
        {
            int index = 0;
            var chemin = mdl.pFichierNomenclature();

            if (File.Exists(chemin))
            {
                using (var sr = new StreamReader(chemin, Encoding.GetEncoding(1252)))
                {
                    // On lit la première ligne contenant l'entête des colonnes
                    String ligne = sr.ReadLine();

                    if (ligne.IsRef())
                    {
                        // On récupère la campagne de départ
                        if (ligne.StartsWith(CONST_PRODUCTION.CAMPAGNE_DEPART_DECOMPTE))
                            ligne = sr.ReadLine();

                        var tab = ligne.Split(new char[] { '\t' });
                        index = tab.Last().eToInteger();
                    }
                }
            }

            return Math.Max(1, index);
        }

        public static ListeSortedCorps pChargerProduction(this ModelDoc2 mdl, String dossierProduction, Boolean mettreAjourCampagne, int campagneDepart = 1)
        {
            var Liste = new ListeSortedCorps();

            Liste.CampagneDepartDecompte = campagneDepart;

            List<String> ListeChemin = new List<String>();

            if (Directory.Exists(dossierProduction))
            {
                var IndiceMax = mdl.pIndiceMaxNomenclature();

                foreach (var d in Directory.EnumerateDirectories(dossierProduction, "*", SearchOption.TopDirectoryOnly))
                {
                    if (!mettreAjourCampagne && ((new DirectoryInfo(d)).Name.eToInteger() == IndiceMax)) continue;

                    var cheminFichier = Path.Combine(d, CONST_PRODUCTION.FICHIER_NOMENC);

                    if (File.Exists(cheminFichier))
                    {
                        using (var sr = new StreamReader(cheminFichier, Encoding.GetEncoding(1252)))
                        {
                            // On lit la première ligne contenant l'entête des colonnes
                            String ligne = sr.ReadLine();

                            if (ligne.IsRef())
                            {
                                // On la split pour récupérer l'indice de la campagne
                                var tab = ligne.Split(new char[] { '\t' });
                                var IndiceCampagne = tab.Last().eToInteger();

                                while ((ligne = sr.ReadLine()) != null)
                                {
                                    if (!String.IsNullOrWhiteSpace(ligne))
                                    {
                                        var c = new Corps(ligne, mdl, IndiceCampagne);

                                        if (Liste.ContainsKey(c.Repere))
                                        {
                                            var tmp = c.Campagne.First();
                                            Liste[c.Repere].Campagne.Add(tmp.Key, tmp.Value);
                                            if (tmp.Key >= campagneDepart)
                                                Liste[c.Repere].Qte += tmp.Value;
                                        }
                                        else
                                        {
                                            Liste.Add(c.Repere, c);

                                            var tmp = c.Campagne.First();
                                            if (tmp.Key >= campagneDepart)
                                                c.Qte = c.Campagne.First().Value;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return Liste;
        }

        public static String pExtPiece = eTypeDoc.Piece.GetEnumInfo<ExtFichier>();

        public static void pCalculerQuantite(this ModelDoc2 mdlBase, ref ListeSortedCorps listeCorps, eTypeCorps typeCorps, List<String> listeMateriaux, List<String> listeDimensions, int indiceCampagne, Boolean mettreAjourCampagne)
        {
            ListeSortedCorps ListeExistant = new ListeSortedCorps();

            if (typeCorps == eTypeCorps.Tole)
                ListeExistant = mdlBase.pChargerProduction(mdlBase.pDossierLaserTole(), mettreAjourCampagne, listeCorps.CampagneDepartDecompte);
            else if (typeCorps == eTypeCorps.Barre)
                ListeExistant = mdlBase.pChargerProduction(mdlBase.pDossierLaserTube(), mettreAjourCampagne, listeCorps.CampagneDepartDecompte);
            else
                return;

            ListeSortedCorps ListeCorpsFiltre = new ListeSortedCorps();
            ListeCorpsFiltre.CampagneDepartDecompte = listeCorps.CampagneDepartDecompte;

            foreach (var corps in listeCorps.Values)
            {
                if ((corps.TypeCorps == typeCorps) &&
                        listeMateriaux.Contains(corps.Materiau) &&
                        listeDimensions.Contains(corps.Dimension)
                        )
                {
                    var qte = corps.Campagne[indiceCampagne];

                    if (qte > 0)
                        corps.Maj = true;

                    if (ListeExistant.ContainsKey(corps.Repere))
                    {
                        if ((qte - ListeExistant[corps.Repere].Qte) == 0)
                            corps.Maj = false;

                        var qteCampagnePrecedente = 0;
                        var corpsExistant = ListeExistant[corps.Repere];
                        foreach (var c in corpsExistant.Campagne)
                        {
                            if ((c.Key >= listeCorps.CampagneDepartDecompte) && (c.Key != indiceCampagne))
                                qteCampagnePrecedente += c.Value;
                        }

                        qte = Math.Max(0, qte - qteCampagnePrecedente);
                    }

                    corps.Qte = qte;
                    ListeCorpsFiltre.Add(corps.Repere, corps);
                }
            }

            listeCorps = ListeCorpsFiltre;
        }

        public static Feature pEsquisseRepere(this ModelDoc2 mdl, Boolean creer = true)
        {
            // On recherche l'esquisse contenant les parametres
            Feature Esquisse = mdl.eChercherFonction(fc => { return fc.Name == CONSTANTES.NOM_ESQUISSE_NUMEROTER; });

            if (Esquisse.IsNull() && creer)
            {
                var SM = mdl.SketchManager;

                // On recherche le chemin du bloc
                String cheminbloc = pCheminBlocEsquisseNumeroter();

                if (String.IsNullOrWhiteSpace(cheminbloc))
                    return null;

                // On supprime la definition du bloc
                pSupprimerDefBloc(mdl, cheminbloc);

                // On recherche le plan de dessus, le deuxième dans la liste des plans de référence
                Feature Plan = mdl.eListeFonctions(fc => { return fc.GetTypeName2() == FeatureType.swTnRefPlane; })[1];

                // Selection du plan et création de l'esquisse
                Plan.eSelect();
                SM.InsertSketch(true);
                SM.AddToDB = false;
                SM.DisplayWhenAdded = true;

                mdl.eEffacerSelection();

                // On récupère la fonction de l'esquisse
                Esquisse = mdl.Extension.GetLastFeatureAdded();

                // On insère le bloc
                MathUtility Mu = App.Sw.GetMathUtility();
                MathPoint Origine = Mu.CreatePoint(new double[] { 0, 0, 0 });
                var def = SM.MakeSketchBlockFromFile(Origine, cheminbloc, false, 1, 0);

                // On récupère la première instance
                // et on l'explose
                var Tab = (Object[])def.GetInstances();
                var ins = (SketchBlockInstance)Tab[0];
                SM.ExplodeSketchBlockInstance(ins);

                // Fermeture de l'esquisse
                SM.AddToDB = false;
                SM.DisplayWhenAdded = true;
                SM.InsertSketch(true);

                //// On supprime la definition du bloc
                //SupprimerDefBloc(mdl, cheminbloc);

                // On renomme l'esquisse
                Esquisse.Name = CONSTANTES.NOM_ESQUISSE_NUMEROTER;

                mdl.eEffacerSelection();

                // On l'active dans toutes les configurations
                Esquisse.SetSuppression2((int)swFeatureSuppressionAction_e.swUnSuppressFeature, (int)swInConfigurationOpts_e.swAllConfiguration, null);
            }

            if (Esquisse.IsRef())
            {
                // On selectionne l'esquisse, on la cache
                // et on la masque dans le FeatureMgr
                // elle ne sera pas du tout acessible par l'utilisateur
                Esquisse.eSelect();
                mdl.BlankSketch();
                //Esquisse.SetUIState((int)swUIStates_e.swIsHiddenInFeatureMgr, true);
                mdl.eEffacerSelection();

                mdl.EditRebuild3();
            }

            return Esquisse;
        }

        private static void pSupprimerDefBloc(ModelDoc2 mdl, String cheminbloc)
        {
            var TabDef = (Object[])mdl.SketchManager.GetSketchBlockDefinitions();
            if (TabDef.IsRef())
            {
                foreach (SketchBlockDefinition blocdef in TabDef)
                {
                    if (blocdef.FileName == cheminbloc)
                    {
                        Feature d = blocdef.GetFeature();
                        d.eSelect();
                        mdl.Extension.DeleteSelection2((int)swDeleteSelectionOptions_e.swDelete_Absorbed);
                        mdl.eEffacerSelection();
                        break;
                    }
                }
            }
        }

        private static String pCheminBlocEsquisseNumeroter()
        {
            return Sw.CheminBloc(CONSTANTES.NOM_BLOCK_ESQUISSE_NUMEROTER);
        }

        public static void pFixerProp(this ModelDoc2 mdl, String repere)
        {
            CustomPropertyManager PM = mdl.ePartDoc().eListeDesFonctionsDePiecesSoudees()[0].CustomPropertyManager;
            PM.ePropAdd(CONSTANTES.REF_DOSSIER, repere);
            PM.ePropAdd(CONSTANTES.DESC_DOSSIER, repere);
            PM.ePropAdd(CONSTANTES.NOM_DOSSIER, repere);
        }

        public static void pAppliquerOptionsDessinLaser(this ModelDoc2 mdlBase, Boolean afficherNotePliage, int tailleInscription)
        {
            LayerMgr LM = mdlBase.GetLayerManager();
            LM.AddLayer(CONSTANTES.CALQUE_GRAVURE, "", 1227327, (int)swLineStyles_e.swLineCONTINUOUS, (int)swLineWeights_e.swLW_LAYER);
            LM.AddLayer(CONSTANTES.CALQUE_QUANTITE, "", 1227327, (int)swLineStyles_e.swLineCONTINUOUS, (int)swLineWeights_e.swLW_LAYER);

            ModelDocExtension ext = mdlBase.Extension;

            ext.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swFlatPatternOpt_ShowFixedFace, 0, false);
            ext.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swShowSheetMetalBendNotes, (int)swUserPreferenceOption_e.swDetailingNoOptionSpecified, afficherNotePliage);

            TextFormat tf = ext.GetUserPreferenceTextFormat(((int)(swUserPreferenceTextFormat_e.swDetailingAnnotationTextFormat)), 0);
            tf.CharHeight = tailleInscription / 1000.0;
            ext.SetUserPreferenceTextFormat((int)swUserPreferenceTextFormat_e.swDetailingAnnotationTextFormat, 0, tf);
        }
    }

    [ClassInterface(ClassInterfaceType.None)]
    public class ListeSortedCorps : SortedDictionary<int, Corps>
    {
        private int _CampagneDepartDecompte = 1;
        public int CampagneDepartDecompte { get { return _CampagneDepartDecompte; } set { _CampagneDepartDecompte = value; } }

        public String ExportNomenclature(int indiceCampagne)
        {
            String texte = Corps.EnteteNomenclature(indiceCampagne, CampagneDepartDecompte);

            foreach (var corps in Values)
            {
                texte += System.Environment.NewLine;
                texte += corps.LigneNomenclature();
            }

            return texte;
        }

        public String ExportProduction(int indiceCampagne)
        {
            String texte = Corps.EnteteCampagne(indiceCampagne);

            foreach (var corps in Values)
            {
                if (corps.Dvp && corps.Qte > 0)
                {
                    texte += System.Environment.NewLine;
                    texte += corps.LigneCampagne();
                }
            }

            return texte;
        }

        public String EcrireNomenclature(String dossier, int indiceCampagne)
        {
            var chemin = Path.Combine(dossier, CONST_PRODUCTION.FICHIER_NOMENC);

            using (var sw = new StreamWriter(chemin, false, Encoding.GetEncoding(1252)))
                sw.Write(ExportNomenclature(indiceCampagne));

            return chemin;
        }

        public String EcrireProduction(String dossier, int indiceCampagne)
        {
            var chemin = Path.Combine(dossier, CONST_PRODUCTION.FICHIER_NOMENC);
            using (var sw = new StreamWriter(chemin, false, Encoding.GetEncoding(1252)))
                sw.Write(ExportProduction(indiceCampagne));

            return chemin;
        }
    }

    public class AnalyseGeomBarre
    {
        public Body2 Corps = null;

        public ModelDoc2 Mdl = null;

        public gPlan PlanSection;
        public gPoint ExtremPoint1;
        public gPoint ExtremPoint2;
        public ListFaceGeom FaceSectionExt = null;
        public List<ListFaceGeom> ListeFaceSectionInt = null;

        public AnalyseGeomBarre(Body2 corps, ModelDoc2 mdl)
        {
            Corps = corps;
            Mdl = mdl;

            AnalyserFaces();
        }

        #region ANALYSE DE LA GEOMETRIE ET RECHERCHE DU PROFIL

        private void AnalyserFaces()
        {
            try
            {
                List<FaceGeom> ListeFaceCorps = new List<FaceGeom>();

                // Tri des faces pour retrouver celles issues de la même
                foreach (var Face in Corps.eListeDesFaces())
                {
                    var faceExt = new FaceGeom(Face);

                    Boolean Ajouter = true;

                    foreach (var f in ListeFaceCorps)
                    {
                        // Si elles sont identiques, la face "faceExt" est ajoutée à la liste
                        // de face de "f"
                        if (f.FaceExtIdentique(faceExt))
                        {
                            Ajouter = false;
                            break;
                        }
                    }

                    // S'il n'y avait pas de face identique, on l'ajoute.
                    if (Ajouter)
                        ListeFaceCorps.Add(faceExt);

                }

                List<FaceGeom> ListeFaceGeom = new List<FaceGeom>();
                PlanSection = RechercherFaceProfil(ListeFaceCorps, ref ListeFaceGeom);
                ListeFaceSectionInt = TrierFacesConnectees(ListeFaceGeom);

                // Plan de la section et infos
                {
                    var v = PlanSection.Normale;
                    Double X = 0, Y = 0, Z = 0;
                    Corps.GetExtremePoint(v.X, v.Y, v.Z, out X, out Y, out Z);
                    ExtremPoint1 = new gPoint(X, Y, Z);
                    v.Inverser();
                    Corps.GetExtremePoint(v.X, v.Y, v.Z, out X, out Y, out Z);
                    ExtremPoint2 = new gPoint(X, Y, Z);
                }

                // =================================================================================

                // On recherche la face exterieure
                // s'il y a plusieurs boucles de surfaces
                if (ListeFaceSectionInt.Count > 1)
                {
                    {
                        // Si la section n'est composé que de cylindre fermé
                        Boolean EstUnCylindre = true;
                        ListFaceGeom Ext = null;
                        Double RayonMax = 0;
                        foreach (var fg in ListeFaceSectionInt)
                        {
                            if (fg.ListeFaceGeom.Count == 1)
                            {
                                var f = fg.ListeFaceGeom[0];

                                if (f.Type == eTypeFace.Cylindre)
                                {
                                    if (RayonMax < f.Rayon)
                                    {
                                        RayonMax = f.Rayon;
                                        Ext = fg;
                                    }
                                }
                                else
                                {
                                    EstUnCylindre = false;
                                    break;
                                }
                            }
                        }

                        if (EstUnCylindre)
                        {
                            FaceSectionExt = Ext;
                            ListeFaceSectionInt.Remove(Ext);
                        }
                        else
                            FaceSectionExt = null;
                    }

                    {
                        // Methode plus longue pour determiner la face exterieur
                        if (FaceSectionExt == null)
                        {
                            // On créer un vecteur perpendiculaire à l'axe du profil
                            var vect = this.PlanSection.Normale;

                            if (vect.X == 0)
                                vect = vect.Vectoriel(new gVecteur(1, 0, 0));
                            else
                                vect = vect.Vectoriel(new gVecteur(0, 0, 1));

                            vect.Normaliser();

                            // On récupère le point extreme dans cette direction
                            Double X = 0, Y = 0, Z = 0;
                            Corps.GetExtremePoint(vect.X, vect.Y, vect.Z, out X, out Y, out Z);
                            var Pt = new gPoint(X, Y, Z);

                            // La liste de face la plus proche est considérée comme la peau exterieur du profil
                            Double distMin = 1E30;
                            foreach (var Ext in ListeFaceSectionInt)
                            {
                                foreach (var fg in Ext.ListeFaceGeom)
                                {
                                    foreach (var f in fg.ListeSwFace)
                                    {
                                        Double[] res = f.GetClosestPointOn(Pt.X, Pt.Y, Pt.Z);
                                        var PtOnSurface = new gPoint(res);

                                        var dist = Pt.Distance(PtOnSurface);
                                        if (dist < 1E-6)
                                        {
                                            distMin = dist;
                                            FaceSectionExt = Ext;
                                            break;
                                        }
                                    }
                                }
                                if (FaceSectionExt.IsRef()) break;
                            }

                            // On supprime la face exterieur de la liste des faces
                            ListeFaceSectionInt.Remove(FaceSectionExt);
                        }
                    }
                }
                else
                {
                    FaceSectionExt = ListeFaceSectionInt[0];
                    ListeFaceSectionInt.RemoveAt(0);
                }
            }
            catch (Exception e) { this.LogErreur(new Object[] { e }); }

        }

        private gPlan RechercherFaceProfil(List<FaceGeom> listeFaceGeom, ref List<FaceGeom> faceExt)
        {
            gPlan? p = null;
            try
            {
                // On recherche les faces de la section
                foreach (var fg in listeFaceGeom)
                {
                    if (EstUneFaceProfil(fg))
                    {
                        faceExt.Add(fg);

                        // Si c'est un cylindre ou une extrusion, on recupère le plan
                        if ((p == null) && (fg.Type == eTypeFace.Cylindre || fg.Type == eTypeFace.Extrusion))
                            p = new gPlan(fg.Origine, fg.Direction);
                    }
                }

                // S'il n'y a que des faces plane, il faut calculer le plan de la section
                // a partir de deux plan non parallèle
                if (p == null)
                {
                    gVecteur? v1 = null;
                    foreach (var fg in faceExt)
                    {
                        if (v1 == null)
                            v1 = fg.Normale;
                        else
                        {
                            var vtmp = ((gVecteur)v1).Vectoriel(fg.Normale);
                            if (Math.Abs(vtmp.Norme) > 1E-8)
                                p = new gPlan(fg.Origine, vtmp);
                        }

                    }
                }
            }
            catch (Exception e) { this.LogErreur(new Object[] { e }); }

            return (gPlan)p;
        }

        private Boolean EstUneFaceProfil(FaceGeom fg)
        {
            foreach (var f in fg.ListeSwFace)
            {
                Byte[] Tab = Mdl.Extension.GetPersistReference3(f);
                String S = System.Text.Encoding.Default.GetString(Tab);

                int Pos_moSideFace = S.IndexOf("moSideFace3IntSurfIdRep_c");

                int Pos_moVertexRef = S.Position("moVertexRef");

                int Pos_moDerivedSurfIdRep = S.Position("moDerivedSurfIdRep_c");

                int Pos_moFromSkt = Math.Min(S.Position("moFromSktEntSurfIdRep_c"), S.Position("moFromSktEnt3IntSurfIdRep_c"));

                int Pos_moEndFace = Math.Min(S.Position("moEndFaceSurfIdRep_c"), S.Position("moEndFace3IntSurfIdRep_c"));

                if (Pos_moSideFace != -1 && Pos_moSideFace < Pos_moEndFace && Pos_moSideFace < Pos_moFromSkt && Pos_moSideFace < Pos_moVertexRef && Pos_moSideFace < Pos_moDerivedSurfIdRep)
                    return true;
            }

            return false;
        }

        private List<ListFaceGeom> TrierFacesConnectees(List<FaceGeom> listeFace)
        {
            List<FaceGeom> listeTmp = new List<FaceGeom>(listeFace);
            List<ListFaceGeom> ListeTri = null;

            if (listeTmp.Count > 0)
            {
                ListeTri = new List<ListFaceGeom>() { new ListFaceGeom(listeTmp[0]) };
                listeTmp.RemoveAt(0);

                while (listeTmp.Count > 0)
                {
                    var l = ListeTri.Last();

                    int i = 0;
                    while (i < listeTmp.Count)
                    {
                        var f = listeTmp[i];

                        if (l.AjouterFaceConnectee(f))
                        {
                            listeTmp.RemoveAt(i);
                            i = -1;
                        }
                        i++;
                    }

                    if (listeTmp.Count > 0)
                    {
                        ListeTri.Add(new ListFaceGeom(listeTmp[0]));
                        listeTmp.RemoveAt(0);
                    }
                }
            }

            // On recherche les cylindres uniques
            // et on les marque comme fermé s'ils ont plus de deux boucle
            foreach (var l in ListeTri)
            {
                if (l.ListeFaceGeom.Count == 1)
                {
                    var f = l.ListeFaceGeom[0];
                    if (f.ListeSwFace.Count == 1)
                    {
                        var cpt = 0;

                        foreach (var loop in f.SwFace.eListeDesBoucles())
                            if (loop.IsOuter()) cpt++;

                        if (cpt > 1)
                            l.Fermer = true;
                    }
                }
            }

            return ListeTri;
        }

        public enum eTypeFace
        {
            Inconnu = 1,
            Plan = 2,
            Cylindre = 3,
            Extrusion = 4
        }

        public enum eOrientation
        {
            Indefini = 1,
            Coplanaire = 2,
            Colineaire = 3,
            MemeOrigine = 4
        }

        public class FaceGeom
        {
            public Face2 SwFace = null;
            private Surface Surface = null;

            public gPoint Origine;
            public gVecteur Normale;
            public gVecteur Direction;
            public Double Rayon = 0;
            public eTypeFace Type = eTypeFace.Inconnu;

            public List<Face2> ListeSwFace = new List<Face2>();

            public List<Face2> ListeFacesConnectee
            {
                get
                {
                    var liste = new List<Face2>();

                    liste.AddRange(ListeSwFace[0].eListeDesFacesContigues());
                    for (int i = 1; i < ListeSwFace.Count; i++)
                    {
                        var l = ListeSwFace[i].eListeDesFacesContigues();

                        foreach (var f in l)
                        {
                            liste.AddIfNotExist(f);
                        }
                    }

                    return liste;
                }
            }

            public FaceGeom(Face2 swface)
            {
                SwFace = swface;

                Surface = (Surface)SwFace.GetSurface();

                ListeSwFace.Add(SwFace);

                switch ((swSurfaceTypes_e)Surface.Identity())
                {
                    case swSurfaceTypes_e.PLANE_TYPE:
                        Type = eTypeFace.Plan;
                        GetInfoPlan();
                        break;

                    case swSurfaceTypes_e.CYLINDER_TYPE:
                        Type = eTypeFace.Cylindre;
                        GetInfoCylindre();
                        break;

                    case swSurfaceTypes_e.EXTRU_TYPE:
                        Type = eTypeFace.Extrusion;
                        GetInfoExtrusion();
                        break;

                    default:
                        break;
                }
            }

            public Boolean FaceExtIdentique(FaceGeom fe, Double arrondi = 1E-10)
            {
                if (Type != fe.Type)
                    return false;

                if (!Origine.Comparer(fe.Origine, arrondi))
                    return false;

                switch (Type)
                {
                    case eTypeFace.Inconnu:
                        return false;
                    case eTypeFace.Plan:
                        if (!Normale.EstColineaire(fe.Normale, arrondi))
                            return false;
                        break;
                    case eTypeFace.Cylindre:
                        if (!Direction.EstColineaire(fe.Direction, arrondi) || (Math.Abs(Rayon - fe.Rayon) > arrondi))
                            return false;
                        break;
                    case eTypeFace.Extrusion:
                        if (!Direction.EstColineaire(fe.Direction, arrondi))
                            return false;
                        break;
                    default:
                        break;
                }

                ListeSwFace.Add(fe.SwFace);
                return true;
            }

            private void GetInfoPlan()
            {
                Boolean Reverse = SwFace.FaceInSurfaceSense();

                if (Surface.IsPlane())
                {
                    Double[] Param = Surface.PlaneParams;

                    if (Reverse)
                    {
                        Param[0] = Param[0] * -1;
                        Param[1] = Param[1] * -1;
                        Param[2] = Param[2] * -1;
                    }

                    Origine = new gPoint(Param[3], Param[4], Param[5]);
                    Normale = new gVecteur(Param[0], Param[1], Param[2]);
                }
            }

            private void GetInfoCylindre()
            {
                if (Surface.IsCylinder())
                {
                    Double[] Param = Surface.CylinderParams;

                    Origine = new gPoint(Param[0], Param[1], Param[2]);
                    Direction = new gVecteur(Param[3], Param[4], Param[5]);
                    Rayon = Param[6];

                    var UV = (Double[])SwFace.GetUVBounds();
                    Boolean Reverse = SwFace.FaceInSurfaceSense();

                    var ev1 = (Double[])Surface.Evaluate((UV[0] + UV[1]) * 0.5, (UV[2] + UV[3]) * 0.5, 0, 0);
                    if (Reverse)
                    {
                        ev1[3] = -ev1[3];
                        ev1[4] = -ev1[4];
                        ev1[5] = -ev1[5];
                    }

                    Normale = new gVecteur(ev1[3], ev1[4], ev1[5]);
                }
            }

            private void GetInfoExtrusion()
            {
                if (Surface.IsSwept())
                {
                    Double[] Param = Surface.GetExtrusionsurfParams();
                    Direction = new gVecteur(Param[0], Param[1], Param[2]);

                    Curve C = Surface.GetProfileCurve();
                    C = C.GetBaseCurve();

                    Double StartParam = 0, EndParam = 0;
                    Boolean IsClosed = false, IsPeriodic = false;

                    if (C.GetEndParams(out StartParam, out EndParam, out IsClosed, out IsPeriodic))
                    {
                        Double[] Eval = C.Evaluate(StartParam);

                        Origine = new gPoint(Eval[0], Eval[1], Eval[2]);
                    }

                    var UV = (Double[])SwFace.GetUVBounds();
                    Boolean Reverse = SwFace.FaceInSurfaceSense();

                    var ev1 = (Double[])Surface.Evaluate((UV[0] + UV[1]) * 0.5, (UV[2] + UV[3]) * 0.5, 0, 0);
                    if (Reverse)
                    {
                        ev1[3] = -ev1[3];
                        ev1[4] = -ev1[4];
                        ev1[5] = -ev1[5];
                    }

                    Normale = new gVecteur(ev1[3], ev1[4], ev1[5]);
                }
            }
        }

        public class ListFaceGeom
        {
            public Boolean Fermer = false;

            public List<FaceGeom> ListeFaceGeom = new List<FaceGeom>();

            public Double DistToExtremPoint1 = 1E30;
            public Double DistToExtremPoint2 = 1E30;

            // Initialisation avec une face
            public ListFaceGeom(FaceGeom f)
            {
                ListeFaceGeom.Add(f);
            }

            public List<Face2> ListeFaceSw()
            {
                var liste = new List<Face2>();

                foreach (var fl in ListeFaceGeom)
                    liste.AddRange(fl.ListeSwFace);

                return liste;
            }

            public Boolean AjouterFaceConnectee(FaceGeom f)
            {
                var Ajouter = false;
                var Connection = 0;

                int r = ListeFaceGeom.Count;

                for (int i = 0; i < r; i++)
                {
                    var l = ListeFaceGeom[i].ListeFacesConnectee;

                    foreach (var swf in f.ListeSwFace)
                    {
                        if (l.eContient(swf))
                        {
                            if (Ajouter == false)
                            {
                                ListeFaceGeom.Add(f);
                                Ajouter = true;
                            }

                            Connection++;
                            break;
                        }
                    }

                }

                if (Connection > 1)
                    Fermer = true;

                return Ajouter;
            }

            public void CalculerDistance(gPoint extremPoint1, gPoint extremPoint2)
            {
                foreach (var f in ListeFaceSw())
                {
                    {
                        Double[] res = f.GetClosestPointOn(extremPoint1.X, extremPoint1.Y, extremPoint1.Z);
                        var dist = extremPoint1.Distance(new gPoint(res));
                        if (dist < DistToExtremPoint1) DistToExtremPoint1 = dist;
                    }

                    {
                        Double[] res = f.GetClosestPointOn(extremPoint2.X, extremPoint2.Y, extremPoint2.Z);
                        var dist = extremPoint2.Distance(new gPoint(res));
                        if (dist < DistToExtremPoint2) DistToExtremPoint2 = dist;
                    }
                }
            }
        }

        private eOrientation Orientation(FaceGeom f1, FaceGeom f2)
        {
            var val = eOrientation.Indefini;
            if (f1.Type == eTypeFace.Plan && f2.Type == eTypeFace.Plan)
            {
                val = Orientation(f1.Origine, f1.Normale, f2.Origine, f2.Normale);
            }
            else if (f1.Type == eTypeFace.Plan && (f2.Type == eTypeFace.Cylindre || f2.Type == eTypeFace.Extrusion))
            {
                gPlan P = new gPlan(f2.Origine, f2.Direction);
                if (P.SurLePlan(f1.Origine, 1E-10) && P.SurLePlan(f1.Origine.Composer(f1.Normale), 1E-10))
                {
                    val = eOrientation.Coplanaire;
                }
            }
            else if (f2.Type == eTypeFace.Plan && (f1.Type == eTypeFace.Cylindre || f1.Type == eTypeFace.Extrusion))
            {
                gPlan P = new gPlan(f1.Origine, f1.Direction);
                if (P.SurLePlan(f2.Origine, 1E-10) && P.SurLePlan(f2.Origine.Composer(f2.Normale), 1E-10))
                {
                    val = eOrientation.Coplanaire;
                }
            }


            return val;
        }

        private eOrientation Orientation(gPoint p1, gVecteur v1, gPoint p2, gVecteur v2)
        {
            if (p1.Distance(p2) < 1E-10)
                return eOrientation.MemeOrigine;

            gVecteur Vtmp = new gVecteur(p1, p2);

            if ((v1.Vectoriel(Vtmp).Norme < 1E-10) && (v2.Vectoriel(Vtmp).Norme < 1E-10))
                return eOrientation.Colineaire;

            gVecteur Vn1 = (new gVecteur(p1, p2)).Vectoriel(v1);
            gVecteur Vn2 = (new gVecteur(p2, p1)).Vectoriel(v2);

            gVecteur Vn = Vn1.Vectoriel(Vn2);

            if (Vn.Norme < 1E-10)
                return eOrientation.Coplanaire;

            return eOrientation.Indefini;
        }

        #endregion
    }

    public class Corps : INotifyPropertyChanged
    {
        public ModelDoc2 MdlBase { get; set; }
        public Body2 SwCorps { get; set; }
        public SortedDictionary<int, int> Campagne = new SortedDictionary<int, int>();
        private int _Repere = -1;
        public int Repere
        {
            get { return _Repere; }
            set { _Repere = value; InitChemins(); }
        }

        public String RepereComplet
        {
            get { return CONSTANTES.PREFIXE_REF_DOSSIER + Repere; }
        }

        public eTypeCorps TypeCorps { get; set; }
        /// <summary>
        /// Epaisseur de la tôle ou section
        /// </summary>
        public String Dimension { get; set; }
        /// <summary>
        /// Longueur de la barre ou volume de la tôle
        /// </summary>
        public String Volume { get; set; }
        public String Materiau { get; set; }
        public ModelDoc2 Modele { get; set; }
        private long _TailleFichier = long.MaxValue;
        public String NomConfig { get; set; }
        public int IdDossier { get; set; }
        public String NomCorps { get; set; }

        public static String EnteteNomenclature(int indiceCampagne, int campagneDepartDecompte)
        {
            String entete = String.Format("{0}\t{1}", CONST_PRODUCTION.CAMPAGNE_DEPART_DECOMPTE, campagneDepartDecompte);
            entete += System.Environment.NewLine;
            entete += String.Format("{0}\t{1}\t{2}\t{3}\t{4}", "Repere", "Type", "Dimension", "Volume", "Materiau");
            for (int i = 0; i < indiceCampagne; i++)
                entete += String.Format("\t{0}", i + 1);

            return entete;
        }

        public string LigneNomenclature()
        {
            String Ligne = String.Format("{0}\t{1}\t{2}\t{3}\t{4}", Repere, TypeCorps, Dimension, Volume, Materiau);

            for (int i = 0; i < Campagne.Keys.Max(); i++)
            {
                int nb = 0;
                if (Campagne.ContainsKey(i + 1))
                    nb = Campagne[i + 1];

                Ligne += String.Format("\t{0}", nb);
            }

            return Ligne;
        }

        public static String EnteteCampagne(int indiceCampagne)
        {
            String entete = String.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}", "Repere", "Type", "Dimension", "Volume", "Materiau", indiceCampagne);
            return entete;
        }

        public string LigneCampagne()
        {
            String Ligne = "";

            if (Dvp)
                Ligne = String.Format("{0}\t{1}\t{2}\t{3}\t{4}\t{5}", Repere, TypeCorps, Dimension, Volume, Materiau, Qte);

            return Ligne;
        }

        public void InitCampagne(int indiceCampagne)
        {
            if (Campagne.ContainsKey(indiceCampagne))
                Campagne[indiceCampagne] = 0;
            else
                Campagne.Add(indiceCampagne, 0);
        }

        public void InitCaracteristiques(BodyFolder dossier, Body2 corps)
        {
            InitDimension(dossier, corps);
            InitVolume(dossier, corps);
        }

        private void InitDimension(BodyFolder dossier, Body2 corps)
        {
            if (TypeCorps == eTypeCorps.Tole)
                Dimension = corps.eEpaisseurCorpsOuDossier(dossier).ToString();
            else
                Dimension = dossier.eProfilDossier();
        }

        private void InitVolume(BodyFolder dossier, Body2 corps)
        {
            if (TypeCorps == eTypeCorps.Tole)
                Volume = String.Format("{0}x{1}", dossier.eLongueurToleDossier(), dossier.eLargeurToleDossier());
            else
                Volume = dossier.eLongueurProfilDossier();
        }

        public Corps(Body2 swCorps, eTypeCorps typeCorps, String materiau, ModelDoc2 mdlBase)
        {
            MdlBase = mdlBase;

            SwCorps = swCorps.Copy2(true);
            TypeCorps = typeCorps;
            Materiau = materiau;
        }

        public Corps(String ligne, ModelDoc2 mdlBase, int indiceCampagne = 1)
        {
            MdlBase = mdlBase;

            var tab = ligne.Split(new char[] { '\t' });
            Repere = tab[0].eToInteger();
            TypeCorps = (eTypeCorps)Enum.Parse(typeof(eTypeCorps), tab[1]);
            Dimension = tab[2];
            Volume = tab[3];
            Materiau = tab[4];

            int cp = indiceCampagne;
            Campagne = new SortedDictionary<int, int>();
            for (int i = 5; i < tab.Length; i++)
                Campagne.Add(cp++, tab[i].eToInteger());
        }

        private void InitChemins()
        {
            _CheminFichierRepere = Path.Combine(MdlBase.pDossierPiece(), RepereComplet + OutilsProd.pExtPiece);
            _CheminFichierApercu = Path.Combine(MdlBase.pDossierPiece(), CONST_PRODUCTION.DOSSIER_PIECES_APERCU, RepereComplet + ".bmp");
            _CheminFichierCorps = Path.Combine(MdlBase.pDossierPiece(), CONST_PRODUCTION.DOSSIER_PIECES_CORPS, RepereComplet + ".data");
            Directory.CreateDirectory(Path.GetDirectoryName(CheminFichierApercu));
            Directory.CreateDirectory(Path.GetDirectoryName(CheminFichierCorps));
        }

        public void AjouterModele(ModelDoc2 mdl, String config, int idDossier, String nomCorps)
        {
            long t = new FileInfo(mdl.GetPathName()).Length;
            if (t < _TailleFichier)
            {
                _TailleFichier = t;
                Modele = mdl;
                NomConfig = config;
                IdDossier = idDossier;
                NomCorps = nomCorps;
            }
        }

        private String _CheminFichierRepere = "";
        public String CheminFichierRepere
        {
            get { return _CheminFichierRepere; }
        }

        private String _CheminFichierApercu = "";
        public String CheminFichierApercu
        {
            get { return _CheminFichierApercu; }
        }

        private String _CheminFichierCorps = "";
        public String CheminFichierCorps
        {
            get { return _CheminFichierCorps; }
        }

        public void ChargerCorps()
        {
            if (File.Exists(CheminFichierCorps))
            {
                Byte[] Tab = File.ReadAllBytes(CheminFichierCorps);
                using (MemoryStream ms = new MemoryStream(Tab))
                {
                    ManagedIStream MgIs = new ManagedIStream(ms);
                    Modeler mdlr = (Modeler)App.Sw.GetModeler();
                    this.SwCorps = (Body2)mdlr.Restore(MgIs);
                }
            }
            else
            {
                // On cherche la première config pliée
                var LstCfg = this.CheminFichierRepere.eListeNomConfiguration();
                var Cfg = "";
                foreach (var c in this.CheminFichierRepere.eListeNomConfiguration())
                {
                    if (c.eEstConfigPliee())
                    {
                        Cfg = c;
                        break;
                    }
                }

                // On ouvre avec la config pliée
                ModelDoc2 mdl = Sw.eOuvrir(this.CheminFichierRepere, Cfg);
                mdl.eActiver();

                var Piece = mdl.ePartDoc();

                // On copie le corps pour qu'il persiste après la fermeture du modèle
                this.SwCorps = Piece.ePremierCorps().Copy2(true);
                // Il faut fermer les modeles sinon SW bug après
                // en avoir ouvert une quarantaine
                mdl.eFermer();
                SauverCorps();
            }
        }

        public void SauverCorps()
        {
            if (File.Exists(CheminFichierCorps)) return;

            try
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    ManagedIStream MgIs = new ManagedIStream(ms);
                    this.SwCorps.Save(MgIs);
                    var Tab = ms.ToArray();
                    File.WriteAllBytes(CheminFichierCorps, Tab);
                }
            }
            catch (Exception ex) { Log.Message(ex); }
        }

        public void SauverRepere(Boolean creerDvp)
        {
            try
            {
                this.Modele.eActiver(swRebuildOnActivation_e.swRebuildActiveDoc);
                this.Modele.ShowConfiguration2(this.NomConfig);
                this.Modele.EditRebuild3();

                // Sauvegarde du fichier de base
                int Errors = 0, Warning = 0;
                this.Modele.Extension.SaveAs(this.CheminFichierRepere, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)(swSaveAsOptions_e.swSaveAsOptions_Copy | swSaveAsOptions_e.swSaveAsOptions_Silent), null, ref Errors, ref Warning);

                var mdlFichier = Sw.eOuvrir(this.CheminFichierRepere, this.NomConfig);
                mdlFichier.eActiver();

                // BreakAllExternalFileReferences2(true); peut causer des plantage !!!!!
                mdlFichier.Extension.BreakAllExternalFileReferences2(false);

                mdlFichier.eActiverManager(false);

                foreach (var nomCfg in mdlFichier.eListeNomConfiguration())
                    if (nomCfg != this.NomConfig)
                        mdlFichier.DeleteConfiguration2(nomCfg);

                var Piece = mdlFichier.ePartDoc();

                if (Piece.eDossierListeDesPiecesSoudees().GetAutomaticUpdate() == false)
                {
                    Piece.eMajListeDesPiecesSoudeesAuto(true);
                    mdlFichier.EditRebuild3();
                }

                SwCorps = null;

                foreach (var c in Piece.eListeCorps(false))
                    if (c.Name == this.NomCorps)
                        SwCorps = c;

                SauverCorps();

                SwCorps.eVisible(true);
                SwCorps.eSelect();
                mdlFichier.FeatureManager.InsertDeleteBody2(true);

                Piece.ePremierCorps(false).eVisible(true);
                mdlFichier.EditRebuild3();
                mdlFichier.pMasquerEsquisses();
                mdlFichier.pFixerProp(this.RepereComplet);

                if ((this.TypeCorps == eTypeCorps.Tole) && creerDvp)
                    this.pCreerDvp(MdlBase.pDossierPiece(), false);

                mdlFichier.FeatureManager.EditFreeze2((int)swMoveFreezeBarTo_e.swMoveFreezeBarToEnd, "", true, true);

                if (this.TypeCorps == eTypeCorps.Tole)
                    OrienterVueTole(mdlFichier);
                else if (this.TypeCorps == eTypeCorps.Barre)
                    OrienterVueBarre(mdlFichier);

                mdlFichier.eActiverManager(true);

                SauverVue(mdlFichier);
                mdlFichier.EditRebuild3();
                mdlFichier.eSauver();
                mdlFichier.eFermer();
            }
            catch (Exception ex) { Log.Message(ex); }
        }

        public void SupprimerFichier()
        {
            File.Delete(this.CheminFichierRepere);
            File.Delete(this.CheminFichierApercu);
            File.Delete(this.CheminFichierCorps);
        }

        private BitmapImage _Apercu = null;
        public BitmapImage Apercu
        {
            get
            {
                if (_Apercu.IsNull())
                    _Apercu = (new Bitmap(_CheminFichierApercu)).ToBitmapImage();

                return _Apercu;
            }

            set { _Apercu = value; }
        }

        private Boolean _Dvp = true;
        public Boolean Dvp
        {
            get { return _Dvp; }
            set
            {
                Set(ref _Dvp, value);
            }
        }

        private String _Qte_Exp = "0";
        public String Qte_Exp
        {
            get { return _Qte_Exp; }
            set
            {
                // Si la valeur se termine par .0 on le supprime
                Regex rgx = new Regex(@"\.0$");
                value = rgx.Replace(value, "");

                // Pour eviter des mises à jour intempestives
                if (Set(ref _Qte_Exp, value) && !Maj_Qte)
                {
                    try
                    {
                        // Pour eviter des calcules intempetifs
                        Double? Eval = value.Evaluer();
                        if (Eval != null)
                            Qte = (int)Eval;
                    }
                    catch { }
                }
            }
        }

        private Boolean Maj_Qte = false;
        private int _Qte = 0;
        public int Qte
        {
            get { return _Qte; }
            set { Set(ref _Qte, value); Maj_Qte = true; Qte_Exp = value.ToString(); Maj_Qte = false; }
        }

        public Boolean Maj = false;

        private String _QteSup_Exp = "0";
        public String QteSup_Exp
        {
            get { return _QteSup_Exp; }
            set
            {
                // Si la valeur se termine par .0 on le supprime
                Regex rgx = new Regex(@"\.0$");
                value = rgx.Replace(value, "");

                // Pour eviter des mises à jour intempestives
                if (Set(ref _QteSup_Exp, value))
                {
                    try
                    {
                        // Pour eviter des calcules intempetifs
                        Double? Eval = value.Evaluer();
                        if (Eval != null)
                            _QteSup = (int)Eval;
                    }
                    catch { }
                }
            }
        }

        private int _QteSup = 0;
        public int QteSup
        {
            get { return _QteSup; }
            set { _QteSup = value; _QteSup_Exp = value.ToString(); }
        }

        public void CalculerDiffPliage(Double volume1, Double volume2)
        {
            var Epaisseur = Dimension.eToDouble();
            var s1 = (volume1 * 1000000000) / Epaisseur;
            var s2 = (volume2 * 1000000000) / Epaisseur;

            DiffPliage = Math.Abs(Math.Round(s1 - s2, 0));
            DiffPliagePct = 0;
            try
            {
                DiffPliagePct = Math.Round(DiffPliage * 100 / Math.Max(s1, s2), 2);
            }
            catch { }
        }

        public Double DiffPliage { get; private set; }

        public Double DiffPliagePct { get; private set; }

        public int NbPli { get; set; }

        private void OrienterVueTole(ModelDoc2 mdl)
        {
            var ListeDepliee = mdl.ePartDoc().eListeFonctionsDepliee();
            if (ListeDepliee.Count > 0)
            {
                var fDepliee = ListeDepliee[0];
                FlatPatternFeatureData fDeplieeInfo = fDepliee.GetDefinition();
                Face2 face = fDeplieeInfo.FixedFace2;
                Surface surface = face.GetSurface();

                Boolean Reverse = face.FaceInSurfaceSense();

                Double[] Param = surface.PlaneParams;

                if (Reverse)
                {
                    Param[0] = Param[0] * -1;
                    Param[1] = Param[1] * -1;
                    Param[2] = Param[2] * -1;
                }

                gVecteur Normale = new gVecteur(Param[0], Param[1], Param[2]);
                MathTransform mtNormale = MathRepere(Normale.MathVector());
                MathTransform mtAxeZ = MathRepere(new gVecteur(1, 1, 1).MathVector()); ;

                MathTransform mtRotate = mtAxeZ.Multiply(mtNormale.Inverse());

                ModelView mv = mdl.ActiveView;
                mv.Orientation3 = mtRotate;
                mv.Activate();
            }
            mdl.ViewZoomtofit2();
            mdl.GraphicsRedraw2();
        }

        private void OrienterVueBarre(ModelDoc2 mdl)
        {
            var corps = mdl.ePartDoc().ePremierCorps();

            var analyse = new AnalyseGeomBarre(corps, mdl);

            MathTransform mtNormale = MathRepere(analyse.PlanSection.Normale.MathVector());
            MathTransform mtAxeZ = MathRepere(new gVecteur(1, 1, 1).MathVector()); ;

            MathTransform mtRotate = mtAxeZ.Multiply(mtNormale.Inverse());

            ModelView mv = mdl.ActiveView;
            mv.Orientation3 = mtRotate;
            mv.Activate();

            mdl.ViewZoomtofit2();
            mdl.GraphicsRedraw2();
        }

        private MathTransform MathRepere(MathVector X)
        {
            MathUtility Mu = App.Sw.GetMathUtility();
            MathVector NormAxeX = null, NormAxeY = null, NormAxeZ = null;

            if (X.ArrayData[0] == 0 && X.ArrayData[2] == 0)
            {
                NormAxeZ = Mu.CreateVector(new Double[] { 0, 1, 0 });
                NormAxeX = Mu.CreateVector(new Double[] { 1, 0, 0 });
                NormAxeY = Mu.CreateVector(new Double[] { 0, 0, -1 });
            }
            else
            {
                NormAxeZ = X.Normalise();
                NormAxeX = Mu.CreateVector(new Double[] { X.ArrayData[2], 0, -1 * X.ArrayData[0] }).Normalise();
                NormAxeY = NormAxeZ.Cross(NormAxeX).Normalise();
            }

            MathVector NormTrans = Mu.CreateVector(new Double[] { 0, 0, 0 });
            MathTransform Mt = Mu.ComposeTransform(NormAxeX, NormAxeY, NormAxeZ, NormTrans, 1);
            return Mt;
        }

        private void SauverVue(ModelDoc2 mdl)
        {
            try
            {
                mdl.SaveBMP(CheminFichierApercu, 0, 0);
                Bitmap bmp = RedimensionnerImage(100, 100, CheminFichierApercu);
                bmp.Save(CheminFichierApercu);
                bmp.Dispose();
            }
            catch (Exception ex) { Log.Message(ex); }
        }

        private Bitmap RedimensionnerImage(int newWidth, int newHeight, string stPhotoPath)
        {
            Bitmap img = new Bitmap(stPhotoPath);
            Bitmap imageSource = img.Clone(new Rectangle(0, 0, img.Width, img.Height), PixelFormat.Format32bppRgb);
            Image imgPhoto = imageSource.AutoCrop();
            img.Dispose();
            imageSource.Dispose();

            int sourceWidth = imgPhoto.Width;
            int sourceHeight = imgPhoto.Height;

            //Consider vertical pics
            if (sourceWidth < sourceHeight)
            {
                int buff = newWidth;

                newWidth = newHeight;
                newHeight = buff;
            }

            int sourceX = 0, sourceY = 0, destX = 0, destY = 0;
            float nPercentW = newWidth / (float)sourceWidth;
            float nPercentH = newHeight / (float)sourceHeight;
            float nPercent;
            if (nPercentH < nPercentW)
            {
                nPercent = nPercentH;
                destX = Convert.ToInt16((newWidth -
                          (sourceWidth * nPercent)) / 2);
            }
            else
            {
                nPercent = nPercentW;
                destY = Convert.ToInt16((newHeight -
                          (sourceHeight * nPercent)) / 2);
            }

            int destWidth = (int)(sourceWidth * nPercent);
            int destHeight = (int)(sourceHeight * nPercent);

            Bitmap bmPhoto = new Bitmap(newWidth, newHeight,
                          PixelFormat.Format32bppRgb);

            bmPhoto.SetResolution(imgPhoto.HorizontalResolution,
                         imgPhoto.VerticalResolution);

            Graphics grPhoto = Graphics.FromImage(bmPhoto);
            grPhoto.Clear(Color.White);
            grPhoto.InterpolationMode = InterpolationMode.HighQualityBicubic;

            grPhoto.DrawImage(imgPhoto,
                new Rectangle(destX, destY, destWidth, destHeight),
                new Rectangle(sourceX, sourceY, sourceWidth, sourceHeight),
                GraphicsUnit.Pixel);

            grPhoto.Dispose();
            imgPhoto.Dispose();
            return bmPhoto;
        }

        #region Notification WPF

        protected bool Set<U>(ref U field, U value, [CallerMemberName]string propertyName = "")
        {
            if (EqualityComparer<U>.Default.Equals(field, value)) { return false; }
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] String NomProp = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(NomProp));
        }

        #endregion
    }
}
