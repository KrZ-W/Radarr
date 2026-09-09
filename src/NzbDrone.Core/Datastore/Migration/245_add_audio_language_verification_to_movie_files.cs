using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // krzw(audio-language-verification): per-track probe outcome stored as JSON, written at import only
    [Migration(245)]
    public class add_audio_language_verification_to_movie_files : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("MovieFiles").AddColumn("AudioLanguageVerification").AsString().Nullable();
        }
    }
}
