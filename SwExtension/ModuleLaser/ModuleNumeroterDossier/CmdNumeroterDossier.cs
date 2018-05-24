using LogDebugging;
using Outils;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwExtension;
using System;
using System.Collections.Generic;

namespace ModuleLaser
{
    namespace ModuleNumeroterDossier
    {
        public class CmdNumeroterDossier : Cmd
        {
            public ModelDoc2 MdlBase = null;
            public Boolean CombinerCorpsIdentiques = false;

            private int indice = 1;

            private String GenRepereDossier
            {
                get
                {
                    return "P" + (indice++).ToString();
                }
            }

            protected override void Command()
            {
                try
                {
                    var ListeCorps = new List<Corps>();

                    if (MdlBase.TypeDoc() == eTypeDoc.Piece)
                    {
                        foreach (var fDossier in MdlBase.ePartDoc().eListeDesFonctionsDePiecesSoudees())
                        {
                            BodyFolder Dossier = fDossier.GetSpecificFeature2();

                            if (Dossier.IsNull() || Dossier.eNbCorps() == 0 || !(Dossier.eEstUnDossierDeBarres() || Dossier.eEstUnDossierDeToles()))
                                continue;

                            fDossier.Name = "P" + (indice++).ToString();
                        }
                    }
                    else
                    {
                        // On ouvre les modeles que l'on veut modifier
                        MdlBase.eComposantRacine().eRecParcourirComposantBase(
                            comp =>
                            {
                                if (comp.TypeDoc() != eTypeDoc.Piece) return;

                                foreach (var fDossier in comp.eListeDesFonctionsDePiecesSoudees())
                                {
                                    BodyFolder Dossier = fDossier.GetSpecificFeature2();
                                    if (Dossier.IsRef() && Dossier.eNbCorps() > 0 && (Dossier.eEstUnDossierDeBarres() || Dossier.eEstUnDossierDeToles()))
                                    {
                                        comp.eModelDoc2().eActiver(swRebuildOnActivation_e.swRebuildActiveDoc);
                                        return;
                                    }
                                }
                            },
                            // On ne parcourt pas les assemblages exclus
                            comp =>
                            {
                                if (comp.ExcludeFromBOM)
                                    return false;

                                return true;
                            }
                            );

                        MdlBase.eActiver(swRebuildOnActivation_e.swRebuildActiveDoc);

                        HashSet<String> DossierTraite = new HashSet<string>();
                        int noDossier = 1;
                        MdlBase.eComposantRacine().eRecParcourirComposantBase(
                            comp =>
                            {
                                if (comp.TypeDoc() != eTypeDoc.Piece) return;

                                foreach (var fDossier in comp.eListeDesFonctionsDePiecesSoudees())
                                {
                                    BodyFolder Dossier = fDossier.GetSpecificFeature2();
                                    if (Dossier.IsRef() && Dossier.eNbCorps() > 0 && (Dossier.eEstUnDossierDeBarres() || Dossier.eEstUnDossierDeToles()))
                                    {
                                        if (fDossier.Name.StartsWith("P") && !fDossier.Name.EndsWith("___XX"))
                                            fDossier.Name = fDossier.Name + "___XX";

                                        CustomPropertyManager PM = fDossier.CustomPropertyManager;
                                        var propVal = String.Format("\"SW-CutListItemName@@@{0}@{1}\"", fDossier.Name, comp.eModelDoc2().eNomAvecExt());
                                        PM.ePropAdd(CONSTANTES.REF_DOSSIER, propVal);

                                        var indexDossier = String.Format("{0}__{1}__{2}", comp.GetPathName(), comp.eNomConfiguration(), fDossier.GetID());
                                        if (!DossierTraite.Contains(indexDossier))
                                        {
                                            PM.ePropAdd(CONSTANTES.NO_DOSSIER, noDossier++);
                                            DossierTraite.Add(indexDossier);
                                        }
                                    }
                                }
                            },
                            // On ne parcourt pas les assemblages exclus
                            comp =>
                            {
                                if (comp.ExcludeFromBOM)
                                    return false;

                                return true;
                            }
                            );

                        int Errors = 0, Warnings = 0;
                        MdlBase.Save3((int)swSaveAsOptions_e.swSaveAsOptions_SaveReferenced + (int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref Errors, ref Warnings);
                        MdlBase.EditRebuild3();

                        // Comparaison des corps
                        // et synthese des dossiers par corps
                        MdlBase.eComposantRacine().eRecParcourirComposantBase(
                            comp =>
                            {
                                if (comp.TypeDoc() != eTypeDoc.Piece) return;

                                var mdl = comp.eModelDoc2();

                                foreach (var fDossier in comp.eListeDesFonctionsDePiecesSoudees())
                                {
                                    BodyFolder Dossier = fDossier.GetSpecificFeature2();
                                    if (Dossier.IsNull() || Dossier.eNbCorps() == 0 || !(Dossier.eEstUnDossierDeBarres() || Dossier.eEstUnDossierDeToles()))
                                        continue;

                                    var SwCorps = Dossier.ePremierCorps();
                                    if (SwCorps.IsNull()) continue;

                                    var MateriauCorps = SwCorps.eGetMateriauCorpsOuComp(comp);

                                    Boolean Ajoute = false;

                                    if (CombinerCorpsIdentiques)
                                    {
                                        foreach (var CorpsTest in ListeCorps)
                                        {
                                            if (MateriauCorps != CorpsTest.Materiau) continue;

                                            if (SwCorps.eEstSemblable(CorpsTest.SwCorps))
                                            {
                                                CorpsTest.Nb += Dossier.eNbCorps();
                                                CorpsTest.AjouterModele(comp, fDossier.GetID());
                                                NommerDossier(fDossier, CorpsTest.Repere);
                                                Ajoute = true;
                                                break;
                                            }
                                        }
                                    }

                                    if (Ajoute == false)
                                    {
                                        var rep = GenRepereDossier;
                                        NommerDossier(fDossier, rep);
                                        var corps = new Corps(SwCorps, MateriauCorps);
                                        corps.Nb = Dossier.eNbCorps();
                                        corps.Repere = rep;
                                        corps.AjouterModele(comp, fDossier.GetID());
                                        ListeCorps.Add(corps);
                                    }
                                }
                            },
                            // On ne parcourt pas les assemblages exclus
                            comp =>
                            {
                                if (comp.ExcludeFromBOM)
                                    return false;

                                return true;
                            }
                            );

                        // Petit r�cap
                        WindowLog.EcrireF("Nb de corps unique : {0}", ListeCorps.Count);
                        int nbtt = 0;
                        foreach (var corps in ListeCorps)
                        {
                            nbtt += corps.Nb;
                            WindowLog.EcrireF("{0} : {1}", corps.Repere, corps.Nb);
                        }

                        WindowLog.EcrireF("Nb total de corps : {0}", nbtt);
                    }
                }
                catch (Exception e) { this.LogMethode(new Object[] { e }); }
            }

            // On rajoute des espaces au nom pour remedier au probl�mes
            // des corps identiques dans des dossiers diff�rents
            private String NommerDossier(Feature f, String rep)
            {
                int Boucle = 0;
                var rtmp = rep;
                f.Name = rtmp;
                while ((f.Name != rtmp) && (Boucle++ < 15))
                {
                    rtmp += " ";
                    f.Name = rtmp;
                }

                return rtmp;
            }

            private class Corps
            {
                public Body2 SwCorps;
                public String Materiau;
                public String Repere;
                public SortedDictionary<ModelDoc2, SortedDictionary<String, Dictionary<int, String>>> ListeModele = new SortedDictionary<ModelDoc2, SortedDictionary<String, Dictionary<int, String>>>(new CompareModelDoc2());
                public int Nb = 0;

                public Corps(Body2 swCorps, String materiau)
                {
                    SwCorps = swCorps;
                    Materiau = materiau;
                }

                public void AjouterModele(ModelDoc2 mdl, String config, int dossier)
                {
                    if (ListeModele.ContainsKey(mdl))
                    {
                        var lCfg = ListeModele[mdl];
                        if (lCfg.ContainsKey(config))
                        {
                            var lDossier = lCfg[config];
                            if (!lDossier.ContainsKey(dossier))
                                lDossier.Add(dossier, Repere);
                        }
                        else
                        {
                            var lDossier = new Dictionary<int, String>();
                            lDossier.Add(dossier, Repere);
                            lCfg.Add(config, lDossier);
                        }
                    }
                    else
                    {
                        var lDossier = new Dictionary<int, String>();
                        lDossier.Add(dossier, Repere);
                        var lCfg = new SortedDictionary<String, Dictionary<int, String>>(new WindowsStringComparer());
                        lCfg.Add(config, lDossier);
                        ListeModele.Add(mdl, lCfg);
                    }
                }

                public void AjouterModele(Component2 comp, int dossier)
                {
                    AjouterModele(comp.eModelDoc2(), comp.eNomConfiguration(), dossier);
                }
            }
        }
    }
}

