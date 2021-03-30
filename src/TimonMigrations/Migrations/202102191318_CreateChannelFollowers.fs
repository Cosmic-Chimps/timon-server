namespace TimonMigrations.Migrations

open FluentMigrator

[<Migration(202102191318L, "CreateChannelFollowers")>]
type CreateChannelFollowers() =
    inherit Migration()

    override this.Up() =
        this
            .Create
            .Table("ChannelFollowers")
            .WithColumn("ChannelId")
            .AsGuid()
            .ForeignKey("Channels", "Id")
            .WithColumn("ActivityPubId")
            .AsString()
        |> ignore

        this
            .Create
            .PrimaryKey("ChannelFollowers_Composite")
            .OnTable("ChannelFollowers")
            .Columns("ChannelId", "ActivityPubId")
        |> ignore

    override this.Down() =
        this.Delete.Table("ChannelFollowers")
        |> ignore

        ()
