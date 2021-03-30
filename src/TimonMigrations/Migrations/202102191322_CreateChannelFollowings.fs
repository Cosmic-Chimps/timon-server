namespace TimonMigrations.Migrations

open FluentMigrator

[<Migration(202102191322L, "CreateChannelFollowings")>]
type CreateChannelFollowings() =
    inherit Migration()

    override this.Up() =
        this
            .Create
            .Table("ChannelFollowings")
            .WithColumn("ChannelId")
            .AsGuid()
            .ForeignKey("Channels", "Id")
            .WithColumn("ActivityPubId")
            .AsString()
        |> ignore

        this
            .Create
            .PrimaryKey("CreateChannelFollowings_Composite")
            .OnTable("CreateChannelFollowings")
            .Columns("ChannelId", "ActivityPubId")
        |> ignore

    override this.Down() =
        this.Delete.Table("ChannelFollowings")
        |> ignore

        ()
