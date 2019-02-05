﻿using LogDebugging;
using Outils;
using SolidWorks.Interop.sldworks;
using SwExtension;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Macros
{
    [ModuleTypeDocContexte(eTypeDoc.Assemblage | eTypeDoc.Piece),
        ModuleTitre("Modifier les lumières"),
        ModuleNom("ModifierLumiere")]
    public class BoutonModifierLumiere : BoutonBase
    {
        public BoutonModifierLumiere()
        {
            LogToWindowLog = false;
        }

        protected override void Command()
        {
            try
            {
                var lcfg = MdlBase.eListeNomConfiguration();

                for (int idLumiere = 0; idLumiere < MdlBase.GetLightSourceCount(); idLumiere++)
                {
                    var nomLumiere = MdlBase.LightSourceUserName[idLumiere];

                    if (nomLumiere.StartsWith("Ambiante"))
                        MajAmbiant(MdlBase, idLumiere, 0.85);
                    else if (nomLumiere.StartsWith("Directionnelle"))
                    {
                        var fl = MdlBase.eFonctionParLeNom(nomLumiere);
                        if(fl.IsRef())
                            fl.eModifierEtat(SolidWorks.Interop.swconst.swFeatureSuppressionAction_e.swSuppressFeature, lcfg);
                    }
                }

                MdlBase.GraphicsRedraw();
            }
            catch (Exception e)
            {
                this.LogErreur(new Object[] { e });
            }
        }

        private void MajAmbiant(ModelDoc2 mdl, int idLumiere, double val)
        {
            var NomSwLumiere = NomLumiere(mdl, idLumiere);
            var PropLumiere = (Double[])mdl.LightSourcePropertyValues[idLumiere];

            MdlBase.SetLightSourcePropertyValuesVB(NomSwLumiere, (int)PropLumiere[0], PropLumiere[1], GetRgb(PropLumiere), 1, PropLumiere[5], PropLumiere[6], PropLumiere[7], PropLumiere[8], PropLumiere[9], PropLumiere[10], PropLumiere[11], 0, 0, 0, val, PropLumiere[16], 0, false);
        }

        private void ActiverLumiere(ModelDoc2 mdl, int idLumiere, Boolean etat)
        {
            var NomSwLumiere = NomLumiere(mdl, idLumiere);
            var PropLumiere = (Double[])mdl.LightSourcePropertyValues[idLumiere];

            MdlBase.SetLightSourcePropertyValuesVB(NomSwLumiere, (int)PropLumiere[0], PropLumiere[1], GetRgb(PropLumiere), 1, PropLumiere[5], PropLumiere[6], PropLumiere[7], PropLumiere[8], PropLumiere[9], PropLumiere[10], PropLumiere[11], 0, 0, 0, PropLumiere[15], PropLumiere[16], 0, !etat);
        }

        private void ModifierDiffLumiere(ModelDoc2 mdl, int idLumiere, Double diff, Double ambiant, Double specular, Boolean etat)
        {
            var NomSwLumiere = NomLumiere(mdl, idLumiere);
            var PropLumiere = (Double[])mdl.LightSourcePropertyValues[idLumiere];

            MdlBase.SetLightSourcePropertyValuesVB(NomSwLumiere, (int)PropLumiere[0], diff, GetRgb(PropLumiere), 1, PropLumiere[5], PropLumiere[6], PropLumiere[7], PropLumiere[8], PropLumiere[9], PropLumiere[10], PropLumiere[11], 0, 0, 0, ambiant, specular, 0, !etat);
        }

        private String NomLumiere(ModelDoc2 mdl, int idLumiere)
        {
            var NomLumiere = MdlBase.GetLightSourceName(idLumiere);
            var result = NomLumiere;

            if (NomLumiere.StartsWith("Ambiante"))
                result = "Ambiante-1";
            else if (NomLumiere.StartsWith("Directionnelle"))
                result = String.Format("Directionnelle-{0}", NomLumiere.Replace("Directionnelle", ""));

            return NomLumiere;
        }

        private int GetRgb(Double[] propLumiere)
        {
            var c = System.Drawing.Color.FromArgb((int)propLumiere[2], (int)propLumiere[3], (int)propLumiere[4]);
            c.ToArgb();
            return 16777215;
        }

    }
}
