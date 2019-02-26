using LogDebugging;
using Outils;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwExtension;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ModuleProduction.ModuleRepererDossier
{
    public class CmdRepererDossier : Cmd
    {
        public ModelDoc2 MdlBase = null;
        public int IndiceCampagne = 0;

        public Boolean ReinitCampagneActuelle = false;
        public Boolean MajCampagnePrecedente = false;
        public Boolean CombinerCorpsIdentiques = false;
        public Boolean CombinerAvecCampagnePrecedente = false;
        public Boolean CreerDvp = false;
        public eTypeCorps FiltrerCorps = eTypeCorps.Piece;

        public ListeSortedCorps ListeCorps = new ListeSortedCorps();

        private int _GenRepereDossier = 0;
        private int GenRepereDossier { get { return ++_GenRepereDossier; } }

        /// <summary>
        /// Pour pouvoir obtenir une r�f�rence unique pour chaque dossier de corps, identiques
        /// dans l'assemblage, on passe par la cr�ation d'une propri�t� dans chaque dossier.
        /// Cette propri�t� est li�e � une cote dans une esquisse dont la valeur est �gale au rep�re.
        /// Suivant la configuration, la valeur de la cote peut changer et donc le rep�re du dossier.
        /// C'est le seul moyen pour avoir un lien entre les dossiers et la configuration du mod�le.
        /// Les propri�t�s des dossiers ne sont pas configurables.
        /// </summary>
        protected override void Command()
        {
            try
            {
                App.Sw.CommandInProgress = true;

                // Si aucun corps n'a d�j� �t� rep�r�, on reinitialise tout
                if (ListeCorps.Count == 0)
                {
                    // On supprime tout les fichiers
                    foreach (FileInfo file in new DirectoryInfo(MdlBase.pDossierPiece()).GetFiles())
                        file.Delete();
                }

                if (ReinitCampagneActuelle && (ListeCorps.Count > 0))
                {
                    // On supprime les rep�res de la campagne actuelle

                    // On recherche les rep�res appartenant aux campagnes pr�c�dentes
                    // pour ne pas supprimer les fichiers
                    // Si la somme des quantit�s des campagnes pr�c�dente est superieure � 0
                    // on garde le rep�re
                    ListeSortedCorps FichierAsauvegarder = new ListeSortedCorps();
                    FichierAsauvegarder.CampagneDepartDecompte = ListeCorps.CampagneDepartDecompte;

                    foreach (var corps in ListeCorps.Values)
                    {
                        int nb = 0;
                        foreach (var camp in corps.Campagne)
                        {
                            if (camp.Key < IndiceCampagne)
                                nb += camp.Value;
                            else
                                break;
                        }

                        if (nb > 0)
                            FichierAsauvegarder.Add(corps.Repere, corps);
                    }

                    // On nettoie les fichiers pr�cedement cr�es
                    foreach (var corps in ListeCorps.Values)
                    {
                        if (FichierAsauvegarder.ContainsKey(corps.Repere)) continue;

                        corps.SupprimerFichier();
                    }

                    ListeCorps = FichierAsauvegarder;

                }

                // On supprime les campagnes superieures � l'indice actuelle
                foreach (var corps in ListeCorps.Values)
                {
                    for (int i = IndiceCampagne; i < corps.Campagne.Keys.Max(); i++)
                    {
                        if (corps.Campagne.ContainsKey(i + 1))
                            corps.Campagne.Remove(i + 1);
                    }
                }


                WindowLog.SautDeLigne();
                WindowLog.EcrireF("Campagne de d�part : {0}", ListeCorps.CampagneDepartDecompte);

                // On charge les corps existant � partir des fichiers
                // et seulement ceux dont la quantit� pour CampagneDepartDecompte est sup�rieure � 0
                if (CombinerCorpsIdentiques && (ListeCorps.Count > 0))
                {
                    var FiltreCampagne = IndiceCampagne;

                    if(CombinerAvecCampagnePrecedente)
                        FiltreCampagne = ListeCorps.CampagneDepartDecompte;

                    WindowLog.SautDeLigne();
                    WindowLog.Ecrire("Chargement des corps existants :");

                    foreach (var corps in ListeCorps.Values)
                    {
                        if (corps.Campagne.ContainsKey(FiltreCampagne) &&
                            (corps.Campagne[FiltreCampagne] > 0) &&
                            File.Exists(corps.CheminFichierRepere)
                            )
                        {
                            WindowLog.EcrireF(" - {0}", corps.RepereComplet);
                            corps.ChargerCorps();
                        }
                    }
                }

                // On reinitialise la quantit� pour la campagne actuelle � 0
                foreach (var corps in ListeCorps.Values)
                    corps.InitCampagne(IndiceCampagne);

                ////////////////////////////////// DEBUT DU REPERAGE ////////////////////////////////////////////////////

                MdlBase.eActiver(swRebuildOnActivation_e.swRebuildActiveDoc);

                MdlBase.pActiverManager(false);

                WindowLog.SautDeLigne();
                WindowLog.Ecrire("Debut du rep�rage");

                // On recherche l'indice de repere max
                if (ListeCorps.Count > 0)
                    _GenRepereDossier = ListeCorps.Keys.Max();

                // On liste les composants
                var ListeComposants = MdlBase.pListerComposants();

                // On boucle sur les modeles
                foreach (var mdl in ListeComposants.Keys)
                {
                    mdl.eActiver(swRebuildOnActivation_e.swRebuildActiveDoc);

                    mdl.pActiverManager(false);

                    // On met � jour les options
                    AppliqueOptionListeDePiecesSoudees(mdl);

                    // On cr�e l'esquisse pour le reperage des dossiers
                    mdl.pEsquisseRepere();

                    // Si le modele est a rep�rer compl�tement
                    Boolean InitModele = true;
                    // On recherche l'index de la dimension maximum
                    int IndexDimension = 0;
                    // On liste les dossiers d�ja trait� pour l'attribution des nouveaux index de dimension
                    HashSet<int> HashPieceIdDossiers = new HashSet<int>();

                    // Les donn�es sont stock�es dans des propri�t�s du mod�le
                    // Le nom du mod�le est stock� dans une propri�t�, si le mod�le est copi�
                    // la propri�t� n'est plus valable, on force le rep�rage
                    // On r�cup�re �galement le dernier indice de la dimension utilis�e
                    if (mdl.ePropExiste(CONST_PRODUCTION.ID_PIECE) && (mdl.eGetProp(CONST_PRODUCTION.ID_PIECE) == mdl.eNomSansExt()))
                    {
                        InitModele = false;
                        if (mdl.ePropExiste(CONST_PRODUCTION.MAX_INDEXDIM))
                            IndexDimension = mdl.eGetProp(CONST_PRODUCTION.MAX_INDEXDIM).eToInteger();

                        if (mdl.ePropExiste(CONST_PRODUCTION.PIECE_ID_DOSSIERS))
                        {
                            var tab = mdl.eGetProp(CONST_PRODUCTION.PIECE_ID_DOSSIERS).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var id in tab)
                                HashPieceIdDossiers.Add(id.eToInteger());
                        }
                    }

                    ////////////////////////////////// BOUCLE SUR LES CONFIGS ////////////////////////////////////////////////////
                    foreach (var nomCfg in ListeComposants[mdl].Keys)
                    {
                        mdl.ShowConfiguration2(nomCfg);
                        mdl.EditRebuild3();
                        WindowLog.SautDeLigne();
                        WindowLog.EcrireF("{0} \"{1}\"", mdl.eNomSansExt(), nomCfg);

                        HashSet<int> HashConfigIdDossiers = new HashSet<int>();

                        Boolean InitConfig = true;

                        int IdCfg = mdl.GetConfigurationByName(nomCfg).GetID();

                        // Idem mod�le, on stock l'id de la config dans une propri�t�.
                        // Si une nouvelle config est cr�e, la valeur de cette propri�t� devient caduc,
                        // on rep�re alors les dossiers
                        // On en profite pour r�cup�rer la liste des ids de dossiers d�j� trait� dans les pr�c�dentes
                        // campagne de rep�rage
                        if (!InitModele && mdl.ePropExiste(CONST_PRODUCTION.ID_CONFIG, nomCfg) && (mdl.eGetProp(CONST_PRODUCTION.ID_CONFIG, nomCfg) == IdCfg.ToString()))
                        {
                            InitConfig = false;
                            if (mdl.ePropExiste(CONST_PRODUCTION.CONFIG_ID_DOSSIERS, nomCfg))
                            {
                                var tab = mdl.eGetProp(CONST_PRODUCTION.CONFIG_ID_DOSSIERS, nomCfg).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var id in tab)
                                    HashConfigIdDossiers.Add(id.eToInteger());
                            }
                        }

                        var piece = mdl.ePartDoc();
                        var ListeDossier = piece.eListeDesFonctionsDePiecesSoudees(
                            swD =>
                            {
                                BodyFolder Dossier = swD.GetSpecificFeature2();

                                // Si le dossier est la racine d'un sous-ensemble soud�, il n'y a rien dedans
                                if (Dossier.IsRef() && (Dossier.eNbCorps() > 0) && !Dossier.eEstExclu() &&
                                FiltrerCorps.HasFlag(Dossier.eTypeDeDossier()))
                                    return true;

                                return false;
                            }
                            );

                        var NbConfig = ListeComposants[mdl][nomCfg];

                        ////////////////////////////////// BOUCLE SUR LES DOSSIERS ////////////////////////////////////////////////////
                        foreach (var fDossier in ListeDossier)
                        {
                            BodyFolder Dossier = fDossier.GetSpecificFeature2();
                            int IdDossier = fDossier.GetID();

                            Dimension param = null;

                            if (!HashPieceIdDossiers.Contains(IdDossier))
                                param = CreerParam(mdl, fDossier, nomCfg, ++IndexDimension);
                            else
                                param = GetParam(mdl, fDossier, nomCfg);

                            var SwCorps = Dossier.ePremierCorps();
                            var NomCorps = SwCorps.Name;
                            var MateriauCorps = SwCorps.eGetMateriauCorpsOuPiece(piece, nomCfg);
                            eTypeCorps TypeCorps = Dossier.eTypeDeDossier();
                            var nbCorps = Dossier.eNbCorps() * NbConfig;

                            int Repere = -1;

                            if (CombinerCorpsIdentiques)
                            {
                                // On recherche s'il existe des corps identiques
                                // Si oui, on applique le m�me rep�re au parametre

                                foreach (var CorpsTest in ListeCorps.Values)
                                {
                                    if (CorpsTest.SwCorps.IsRef() &&
                                        (CombinerAvecCampagnePrecedente || CorpsTest.Campagne.ContainsKey(IndiceCampagne)) &&
                                        (MateriauCorps == CorpsTest.Materiau) &&
                                        (TypeCorps == CorpsTest.TypeCorps) &&
                                        SwCorps.eEstSemblable(CorpsTest.SwCorps))
                                    {
                                        Repere = CorpsTest.Repere;
                                        SetRepere(param, CorpsTest.Repere, nomCfg);
                                        break;
                                    }
                                }
                            }

                            // Initialisation du rep�re
                            if (Repere.eEstNegatif())
                            {
                                // A tester
                                // Si on est mode "MajCampagnePrecedente", �a �vite de rep�rer une seconde fois les pi�ces

                                // Si on est pas en mode "Combiner les corps"
                                // on recup�re le rep�re du dossier
                                // Sinon c'est forc�ment un nouveau rep�re
                                if (!CombinerCorpsIdentiques)
                                    Repere = GetRepere(param, nomCfg);

                                // Cr�ation d'un nouveau rep�re suivant conditions
                                // Dans tous les cas, si la cl� est n�gative, on cr�e un nouveau rep�re
                                if (Repere.eEstNegatif() ||
                                    InitConfig ||
                                    !HashConfigIdDossiers.Contains(IdDossier) ||
                                    !ListeCorps.ContainsKey(Repere))
                                {
                                    Repere = GenRepereDossier;
                                    SetRepere(param, Repere, nomCfg);
                                }
                            }

                            // Initialisation du corps
                            Corps corps = null;
                            if (!ListeCorps.ContainsKey(Repere))
                            {
                                corps = new Corps(SwCorps, TypeCorps, MateriauCorps, MdlBase);
                                corps.InitCampagne(IndiceCampagne);
                                ListeCorps.Add(Repere, corps);
                            }
                            else
                                corps = ListeCorps[Repere];

                            corps.Campagne[IndiceCampagne] += nbCorps;
                            corps.Repere = Repere;
                            corps.InitCaracteristiques(Dossier, SwCorps);
                            corps.AjouterModele(mdl, nomCfg, IdDossier, NomCorps);

                            HashPieceIdDossiers.Add(IdDossier);
                            HashConfigIdDossiers.Add(IdDossier);

                            WindowLog.EcrireF(" - {1} -> {0}", fDossier.Name, corps.RepereComplet);
                        }
                        mdl.ePropAdd(CONST_PRODUCTION.ID_CONFIG, IdCfg, nomCfg);
                        mdl.ePropAdd(CONST_PRODUCTION.CONFIG_ID_DOSSIERS, String.Join(" ", HashConfigIdDossiers), nomCfg);
                    }
                    mdl.ePropAdd(CONST_PRODUCTION.ID_PIECE, mdl.eNomSansExt());
                    mdl.ePropAdd(CONST_PRODUCTION.PIECE_ID_DOSSIERS, String.Join(" ", HashPieceIdDossiers));
                    mdl.ePropAdd(CONST_PRODUCTION.MAX_INDEXDIM, IndexDimension);

                    mdl.pActiverManager(true);
                    mdl.eSauver();
                    mdl.eFermer();
                }

                MdlBase.pActiverManager(true);
                MdlBase.eActiver(swRebuildOnActivation_e.swRebuildActiveDoc);
                MdlBase.EditRebuild3();
                MdlBase.eSauver();

                ////////////////////////////////// FIN DU REPERAGE ////////////////////////////////////////////////////

                // On fermer les fichiers charg�
                foreach (var corps in ListeCorps.Values)
                     Sw.eFermer(corps.CheminFichierRepere);

                WindowLog.SautDeLigne();
                if (ListeCorps.Count > 0)
                    WindowLog.EcrireF("Nb de rep�res : {0}", ListeCorps.Keys.Max());
                else
                    WindowLog.Ecrire("Aucun corps rep�r�");

                // S'il n'y a aucun corps, on se barre
                if (ListeCorps.Count == 0)
                    return;

                ////////////////////////////////// EXPORTER LES CORPS /////////////////////////////////////////////////

                WindowLog.SautDeLigne();
                WindowLog.Ecrire("Export des corps :");

                foreach (var corps in ListeCorps.Values)
                {
                    if (corps.Modele.IsNull()) continue;

                    // Si on est pas en mode "Combiner corps identique" et que le fichier existe
                    // on le supprime pour le mettre � jour, sinon on peut se retrouver
                    // avec des fichiers ne correpondants pas au corps
                    if (!CombinerCorpsIdentiques)
                        corps.SupprimerFichier();

                    // Si le fichier existe, on passe au suivant
                    if (File.Exists(corps.CheminFichierRepere))
                        continue;

                    WindowLog.EcrireF("- {0} export�", CONSTANTES.PREFIXE_REF_DOSSIER + corps.Repere);

                    corps.SauverRepere(CreerDvp);
                }

                ////////////////////////////////// RECAP /////////////////////////////////////////////////

                // Petit r�cap
                WindowLog.SautDeLigne();
                WindowLog.EcrireF("Nb de corps unique : {0}", ListeCorps.Count);

                int nbtt = 0;

                foreach (var corps in ListeCorps.Values)
                {
                    nbtt += corps.Campagne[IndiceCampagne];
                    if (corps.Campagne[IndiceCampagne] > 0)
                        WindowLog.EcrireF("{2} P{0} �{1}", corps.Repere, corps.Campagne[IndiceCampagne], IndiceCampagne);
                }

                WindowLog.EcrireF("Nb total de corps : {0}", nbtt);

                ////////////////////////////////// SAUVEGARDE DE LA NOMENCLATURE /////////////////////////////////////////////

                ListeCorps.EcrireNomenclature(MdlBase.pDossierPiece(), IndiceCampagne);

                ////////////////////////////////////////////////////////////////////////////////////////////////////////////

                MdlBase.eActiver(swRebuildOnActivation_e.swRebuildActiveDoc);

                App.Sw.CommandInProgress = false;

                var aff = new AffichageElementWPF(ListeCorps, IndiceCampagne);
                aff.ShowDialog();
            }
            catch (Exception e) { this.LogErreur(new Object[] { e }); }
        }

        private Dimension GetParam(ModelDoc2 mdl, Feature fDossier, String nomCfg)
        {
            Dimension param = null;

            try
            {
                Func<String, String> ExtractNomParam = delegate (String s)
                {
                    s = s.Replace(CONSTANTES.PREFIXE_REF_DOSSIER + "\"", "").Replace("\"", "");
                    var t = s.Split('@');
                    if (t.Length > 2)
                        return String.Format("{0}@{1}", t[0], t[1]);

                    this.LogErreur(new Object[] { "Pas de parametre dans la reference dossier" });
                    return "";
                };

                // On recherche si le dossier contient d�j� la propri�t� RefDossier
                //      Si non, on ajoute la propri�t� au dossier selon le mod�le suivant :
                //              P"D1@REPERAGE_DOSSIER@Nom_de_la_piece.SLDPRT"
                //      Si oui, on r�cup�re le nom du param�tre � configurer

                CustomPropertyManager PM = fDossier.CustomPropertyManager;
                String val;

                String result = ""; Boolean wasResolved, link;
                var r = PM.Get6(CONSTANTES.REF_DOSSIER, false, out val, out result, out wasResolved, out link);
                String nomParam = ExtractNomParam(val);

                PM.ePropAdd(CONSTANTES.DESC_DOSSIER, val);
                val = String.Format("\"SW-CutListItemName@@@{0}@{1}\"", fDossier.Name, mdl.eNomAvecExt());
                PM.ePropAdd(CONSTANTES.NOM_DOSSIER, val);

                param = mdl.Parameter(nomParam);
                param.SetSystemValue3(0.5 * 0.001, (int)swSetValueInConfiguration_e.swSetValue_InSpecificConfigurations, nomCfg);
            }
            catch (Exception e) { this.LogErreur(new Object[] { e }); }

            return param;
        }

        private Dimension CreerParam(ModelDoc2 mdl, Feature fDossier, String nomCfg, int indexDimension)
        {
            Dimension param = null;

            try
            {
                // On recherche si le dossier contient d�j� la propri�t� RefDossier
                //      Si non, on ajoute la propri�t� au dossier selon le mod�le suivant :
                //              P"D1@REPERAGE_DOSSIER@Nom_de_la_piece.SLDPRT"
                //      Si oui, on r�cup�re le nom du param�tre � configurer

                CustomPropertyManager PM = fDossier.CustomPropertyManager;
                String val;

                String nomParam = String.Format("D{0}@{1}", indexDimension, CONSTANTES.NOM_ESQUISSE_NUMEROTER);
                val = String.Format("{0}\"{1}@{2}\"", CONSTANTES.PREFIXE_REF_DOSSIER, nomParam, mdl.eNomAvecExt());
                var r = PM.ePropAdd(CONSTANTES.REF_DOSSIER, val);

                PM.ePropAdd(CONSTANTES.DESC_DOSSIER, val);
                val = String.Format("\"SW-CutListItemName@@@{0}@{1}\"", fDossier.Name, mdl.eNomAvecExt());
                PM.ePropAdd(CONSTANTES.NOM_DOSSIER, val);

                param = mdl.Parameter(nomParam);
                param.SetSystemValue3(0.5 * 0.001, (int)swSetValueInConfiguration_e.swSetValue_InSpecificConfigurations, nomCfg);
            }
            catch (Exception e) { this.LogErreur(new Object[] { e }); }

            return param;
        }

        private void AppliqueOptionListeDePiecesSoudees(ModelDoc2 mdl)
        {
            mdl.Extension.SetUserPreferenceToggle(((int)(swUserPreferenceToggle_e.swDisableDerivedConfigurations)), 0, false);
            mdl.Extension.SetUserPreferenceToggle(((int)(swUserPreferenceToggle_e.swWeldmentRenameCutlistDescriptionPropertyValue)), 0, true);
            mdl.Extension.SetUserPreferenceToggle(((int)(swUserPreferenceToggle_e.swWeldmentCollectIdenticalBodies)), 0, true);
            mdl.Extension.SetUserPreferenceString(((int)(swUserPreferenceStringValue_e.swSheetMetalDescription)), 0, "T�le");
        }

        private int GetRepere(Dimension param, String nomCfg)
        {
            Double val = (Double)param.GetSystemValue3((int)swSetValueInConfiguration_e.swSetValue_InSpecificConfigurations, nomCfg)[0];
            
            if (!val.eEstInteger())
                val = -1;
            else
                val *= 1000;

            return (int)val;
        }

        private void SetRepere(Dimension param, int val, String nomCfg)
        {
            param.SetSystemValue3(val * 0.001, (int)swSetValueInConfiguration_e.swSetValue_InSpecificConfigurations, nomCfg);
        }
    }
}


