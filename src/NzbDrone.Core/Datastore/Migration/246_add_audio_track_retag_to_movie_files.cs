using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // krzw(audio-track-retag): outcome of the post-import mkvpropedit retag, stored as JSON
    [Migration(246)]
    public class add_audio_track_retag_to_movie_files : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("MovieFiles").AddColumn("AudioTrackRetag").AsString().Nullable();
        }
    }
}
