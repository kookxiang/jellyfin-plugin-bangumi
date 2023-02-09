using System.Collections.Generic;
using System.Linq;
using AnitomySharp;

namespace Jellyfin.Plugin.Bangumi
{
    /// <summary>
    /// The Anitomy class contains methods for extracting various elements from a string path using the AnitomySharp library.
    /// </summary>
    public class Anitomy
    {
        // This variable stores the elements obtained from parsing a file path using the AnitomySharp library
        private IEnumerable<Element> _elements;

        // The constructor takes a file path as input and calls the AnitomySharp.Parse method to parse the file and store the result in the _elements variable
        public Anitomy(string path)
        {
            _elements = AnitomySharp.AnitomySharp.Parse(path);
        }

        // This method returns a List of Element objects from the _elements variable
        public List<Element> GetElements()
        {
            return new List<Element>(_elements);
        }

        /// <summary>
        /// Extracts the ElementAnimeSeason from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementAnimeSeason, or null if not found.</returns>
        public string? ExtractAnimeSeason()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementAnimeSeason)?.Value;
        }

        /// <summary>
        /// Extracts the ElementAnimeSeasonPrefix from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementAnimeSeasonPrefix, or null if not found.</returns>
        public string? ExtractAnimeSeasonPrefix()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementAnimeSeasonPrefix)?.Value;
        }

        /// <summary>
        /// Extracts the ElementAnimeTitle from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementAnimeTitle, or null if not found.</returns>
        public string? ExtractAnimeTitle()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementAnimeTitle)?.Value;
        }
        /// <summary>
        /// Extracts the ElementAnimeType from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementAnimeType, or null if not found.</returns>
        public string? ExtractAnimeType()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementAnimeType)?.Value;
        }

        /// <summary>
        /// Extracts the ElementAnimeYear from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementAnimeYear, or null if not found.</returns>
        public string? ExtractAnimeYear()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementAnimeYear)?.Value;
        }

        /// <summary>
        /// Extracts the ElementAudioTerm from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementAudioTerm, or null if not found.</returns>
        public string? ExtractAudioTerm()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementAudioTerm)?.Value;
        }

        /// <summary>
        /// Extracts the ElementDeviceCompatibility from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementDeviceCompatibility, or null if not found.</returns>
        public string? ExtractDeviceCompatibility()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementDeviceCompatibility)?.Value;
        }

        /// <summary>
        /// Extracts the ElementEpisodeNumber from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementEpisodeNumber, or null if not found.</returns>
        public string? ExtractEpisodeNumber()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementEpisodeNumber)?.Value;
        }

        /// <summary>
        /// Extracts the ElementEpisodeNumberAlt from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementEpisodeNumberAlt, or null if not found.</returns>
        public string? ExtractEpisodeNumberAlt()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementEpisodeNumberAlt)?.Value;
        }

        /// <summary>
        /// Extracts the ElementEpisodePrefix from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementEpisodePrefix, or null if not found.</returns>
        public string? ExtractEpisodePrefix()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementEpisodePrefix)?.Value;
        }

        /// <summary>
        /// Extracts the ElementEpisodeTitle from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementEpisodeTitle, or null if not found.</returns>
        public string? ExtractEpisodeTitle()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementEpisodeTitle)?.Value;
        }

        /// <summary>
        /// Extracts the ElementFileChecksum from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementFileChecksum, or null if not found.</returns>
        public string? ExtractFileChecksum()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementFileChecksum)?.Value;
        }

        /// <summary>
        /// Extracts the ElementFileExtension from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementFileExtension, or null if not found.</returns>
        public string? ExtractFileExtension()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementFileExtension)?.Value;
        }

        /// <summary>
        /// Extracts the ElementFileName from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementFileName, or null if not found.</returns>
        public string? ExtractFileName()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementFileName)?.Value;
        }

        /// <summary>
        /// Extracts the ElementLanguage from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementLanguage, or null if not found.</returns>
        public string? ExtractLanguage()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementLanguage)?.Value;
        }

        /// <summary>
        /// Extracts the ElementOther from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementOther, or null if not found.</returns>
        public string? ExtractOther()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementOther)?.Value;
        }

        /// <summary>
        /// Extracts the ElementReleaseGroup from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementReleaseGroup, or null if not found.</returns>
        public string? ExtractReleaseGroup()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementReleaseGroup)?.Value;
        }

        /// <summary>
        /// Extracts the ElementReleaseInformation from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementReleaseInformation, or null if not found.</returns>
        public string? ExtractReleaseInformation()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementReleaseInformation)?.Value;
        }

        /// <summary>
        /// Extracts the ElementReleaseVersion from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementReleaseVersion, or null if not found.</returns>
        public string? ExtractReleaseVersion()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementReleaseVersion)?.Value;
        }

        /// <summary>
        /// Extracts the ElementSource from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementSource, or null if not found.</returns>
        public string? ExtractSource()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementSource)?.Value;
        }

        /// <summary>
        /// Extracts the ElementSubtitles from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementSubtitles, or null if not found.</returns>
        public string? ExtractSubtitles()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementSubtitles)?.Value;
        }

        /// <summary>
        /// Extracts the ElementUnknown from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementUnknown, or null if not found.</returns>
        public string? ExtractUnknown()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementUnknown)?.Value;
        }

        /// <summary>
        /// Extracts the ElementVideoResolution from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementVideoResolution, or null if not found.</returns>
        public string? ExtractVideoResolution()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementVideoResolution)?.Value;
        }

        /// <summary>
        /// Extracts the ElementVideoTerm from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementVideoTerm, or null if not found.</returns>
        public string? ExtractVideoTerm()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementVideoTerm)?.Value;
        }

        /// <summary>
        /// Extracts the ElementVolumeNumber from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementVolumeNumber, or null if not found.</returns>
        public string? ExtractVolumeNumber()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementVolumeNumber)?.Value;
        }

        /// <summary>
        /// Extracts the ElementVolumePrefix from the given string path.
        /// </summary>
        /// <param name="path">The string path to parse.</param>
        /// <returns>The extracted ElementVolumePrefix, or null if not found.</returns>
        public string? ExtractVolumePrefix()
        {
            return _elements.FirstOrDefault(p => p.Category == Element.ElementCategory.ElementVolumePrefix)?.Value;
        }


    }
}
