using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DiscrepancyScanner
{
    public static class TextHandling
    {
        public static double getAsFromText(string value, char specialIdentfier = '#')
        {   int indexOfSeprator=0;
            int number = 0;
            int Diameter = 0;
            if (value != null)
            {

                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];

                    if ((c == 't' && haveNumberBeforeAndAfterIt(i, value)) || (c == 'T' && haveNumberBeforeAndAfterIt(i, value)
                        || (c == specialIdentfier && haveNumberBeforeAndAfterIt(i, value))))
                    {
                        indexOfSeprator = i;
                        break;
                    }
                }
                if (indexOfSeprator > 0)
                {
                    number = getNumberBeforeThisChar(indexOfSeprator, value);
                    Diameter = getNumberAfterThisChar(indexOfSeprator, value);


                }
                double area = Math.PI * Diameter * Diameter / 4d;
                return Math.Round( number * area);
            }
            else
            {
                return 0;
            }
        }

        private static int getNumberAfterThisChar(int indexOfSeprator, string value)
        {
            string myString = "";

            char c = ' ';
            bool startedCounting = false;

            for (int i = indexOfSeprator + 1; i < value.Length; i++)
            {
                c = value[i];
                if (Char.IsDigit(c))
                {

                    myString += c;
                    startedCounting = true;

                }
                else if (startedCounting)
                {
                    break;
                }


            }

            return int.Parse(myString);

        }

        private static int getNumberBeforeThisChar(int first_T_Index,  string value)
        {
            string myString = "";

            char c = ' ';
            bool startedCounting = false;

            for (int i = first_T_Index - 1; i >= 0; i--)
            {
                c = value[i];
                if (Char.IsDigit(c))
                {

                    myString += c;
                    startedCounting = true;

                }
                else if (startedCounting)
                {
                    break;
                }


            }

            return int.Parse( ReverseString(myString));
        }

        public static string ReverseString(string s)
        {
            char[] charArray = s.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }

        internal static double getAsForLinks(string linksText, double width, out int barSize, char specialIdentfier = '#')
        {
            int spacingIndex = 0;
            int BarTypeIndex = 0;
            double number = 0;
            int Diameter = 0;
            int numberOfBranches = 2;

            if (linksText != null)
            {


                for (int i = 0; i < linksText.Length; i++)
                {
                    char c = linksText[i];

                    if ((c == '@' && haveNumberBeforeAndAfterIt(i, linksText)) || (c == specialIdentfier && haveNumberBeforeAndAfterIt(i, linksText)))
                    {
                        spacingIndex = i;
                        break;
                    }
                }

                for (int i = 0; i < linksText.Length; i++)
                {
                    char c = linksText[i];

                    if ((c == 'y') || (c == 'Y') || (c == 'l') || (c == 'L') ||
                        (c == 't') || (c == 'T'))
                    {
                        BarTypeIndex = i;
                        break;
                    }
                }

                if (spacingIndex > 0)
                {
                    Diameter = getNumberBeforeThisChar(spacingIndex, linksText);
                    number = (double) 1000 / getNumberAfterThisChar(spacingIndex, linksText);
                }

                if (BarTypeIndex != 0)
                {
                    numberOfBranches = getNumberBeforeThisChar(BarTypeIndex, linksText);
                }
                else if (BarTypeIndex == 0)
                {
                    numberOfBranches = RevitEngine.getNumberOfBranches(width);
                    

                }

                double NormalizationFactor = getLinksNormalizationFactor(Diameter); // normalize to fy = 240

                barSize = Diameter;
                double area = Math.PI * Diameter * Diameter / 4d;

                return Math.Round(number * area * numberOfBranches * NormalizationFactor);
            }
            else
            {
                barSize = Diameter;
                return 0;
            }

        }

        public static double getLinksNormalizationFactor(int diameter)
        {
            if (diameter == 8)
            {
                return 1;
            }
            else
            {
                var pref = PrefrencesData.getPrefrencesData();

                return pref.fyForHeighStrength / pref.fyForMield;
            }
        }

        private static bool haveNumberBeforeAndAfterIt(int i,string value)
        {
            bool after = false;
            bool before = false;
            after = haveNumberAfterIt(i, value);
            before = haveNumberBeforeIt(i, value);

            return (after && before);

        }

        private static bool haveNumberBeforeIt(int i, string v)
        {
            i--;
            while (i >= 0)
            {


                if (Char.IsDigit(v[i]))
                {
                    return true;

                }

                else if (v[i] == ' ')
                {
                    // ignoring wite spaces
                }
                else
                {
                    return false;
                }

                i--;
            }
            return false;
        }

        private static bool haveNumberAfterIt(int i, string value)
        {
            i++;
            while (i < value.Length - 1)
            {


                if (Char.IsDigit(value[i]))
                {
                    return true;

                }

                else if (value[i] == ' ')
                {
                    // ignoring wite spaces
                }
                else
                {
                    return false;
                }

                i++;
            }
            return false;

        }
    }
}
