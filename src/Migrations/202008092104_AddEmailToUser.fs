namespace Links.Migrations

open FluentMigrator

[<Migration(202008092104L, "AddEmailToUser")>]
type AddEmailToUser() =
    inherit Migration()

    override this.Up () =
        this.Alter.Table("Users").AddColumn("Email").AsString().NotNullable() |> ignore


    override this.Down () =
        this.Delete.Column("Email").FromTable("Users") |> ignore

