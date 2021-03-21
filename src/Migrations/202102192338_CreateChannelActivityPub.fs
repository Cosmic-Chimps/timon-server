namespace Links.Migrations

open FluentMigrator

[<Migration(202102192338L, "CreateChannelActivityPub")>]
type CreateChannelActivityPub() =
    inherit Migration()

    override this.Up() =
        this.Create.Table("ChannelActivityPub")
            .WithColumn("ChannelId").AsGuid().ForeignKey("Channels", "Id")
            .WithColumn("Username").AsString()
            .WithColumn("Password").AsString()
            .WithColumn("ActivityPubId").AsString()
            |> ignore

    override this.Down() =
        this.Delete.Table("ChannelActivityPub") |> ignore
