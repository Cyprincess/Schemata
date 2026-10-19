# SCIM surface matrix

This matrix records the HTTP surface served by `Schemata.Scim` and `Schemata.Identity.Scim`, the
response each operation produces, and the integration test that covers it. The protocol context and
wire shapes are in [SCIM provisioning](scim.md).

## Common rules

| Item                | Value                                                                                            |
| ------------------- | ------------------------------------------------------------------------------------------------ |
| Base path           | `/scim/v2`, fixed by `ScimConstants.BasePath`                                                     |
| Media type          | `application/scim+json` on every SCIM response                                                    |
| Resource `id`       | The Identity `Uid` as a GUID string; server assigned and unchanged across replace                 |
| `meta.location`     | Absolute URI built from scheme, host, path base, base path, collection, and id                   |
| `Content-Location`  | Mirrors `meta.location` for single-resource responses and create responses                       |
| `Location`          | Set on `POST` to the created resource location                                                   |
| `ETag`              | Never emitted; `meta.version` is never emitted                                                   |
| Unrouted SCIM path  | `404` with a SCIM error body, written by `ScimErrorMiddleware`                                    |
| Undeclared method   | `405` with a SCIM error body when a route template matches but the method does not               |
| Unknown query key   | Ignored, following the RFC 7644 §3.4.2 guidance to ignore unrecognized query parameters          |
| Resource auth        | `401` for an unauthenticated caller; `403` when the policy denies access or is not registered      |

## Discovery

| Operation                           | Auth | Success | Body                                                   | Evidence                                                                          |
| ----------------------------------- | ---- | ------- | ------------------------------------------------------ | --------------------------------------------------------------------------------- |
| `GET /scim/v2/ServiceProviderConfig` | none | 200     | `ServiceProviderConfig` with `schemas` and `meta`       | `ScimDiscoveryShould.Serve_Discovery_Endpoints_With_The_Scim_Media_Type`, `Declare_Unsupported_Optional_Capabilities` |
| `GET /scim/v2/ResourceTypes`         | none | 200     | `ListResponse` of resource types                        | `ScimDiscoveryShould.List_Resource_Types_With_Id_And_Meta`                        |
| `GET /scim/v2/ResourceTypes/{id}`    | none | 200     | One `ResourceType`; `{id}` matches id or name           | `ScimDiscoveryShould.Return_A_Single_Resource_Type_By_Id_Or_Name`                 |
| `GET /scim/v2/Schemas`               | none | 200     | `ListResponse` of schemas                               | `ScimDiscoveryShould.List_Installed_Schemas_With_Schema_Id_And_Meta`              |
| `GET /scim/v2/Schemas/{id}`          | none | 200     | One `Schema`                                            | `ScimDiscoveryShould.Return_A_Single_Schema`                                      |

The `none` auth value marks the controller boundary: the discovery controller carries no SCIM
authorization filter, and a host global fallback policy, when configured, still applies. Discovery
error cases: an unknown resource type id or schema URI returns `404` with the Error schema
(`ScimDiscoveryShould.Serve_Discovery_For_An_Empty_Registry` and
`ScimErrorShould.Omit_The_Keyword_When_No_Scim_Type_Applies`); a non-GET method on a discovery path
returns `405` with a SCIM body (`ScimErrorShould.Reject_Undeclared_Discovery_Methods`).
Discovery paths accept `filter`, `sortBy`, `startIndex`, `count`, `attributes`, and
`excludedAttributes` without acting on them, because the actions declare no query parameters.

The `ServiceProviderConfig` declares `patch`, `bulk`, `filter`, `changePassword`, `sort`, and `etag`
as `supported: false`; `bulk.maxOperations` and `bulk.maxPayloadSize` are `0`, and
`filter.maxResults` is `0` (`ScimDiscoveryShould.Declare_Unsupported_Optional_Capabilities`).

## Users

| Operation                | Request attributes                          | Success | Errors                                                                                         | Evidence                                                                                       |
| ------------------------ | ------------------------------------------- | ------- | ---------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `POST /scim/v2/Users`    | `userName` (required), `displayName`, `active`, `password` | 201 | `400 invalidValue` (bad user name or password), `400 invalidSyntax`, `409 uniqueness`, `415 invalidSyntax`, `401`, `403` | `ScimUsersShould.Create_Get_List_Replace_And_Delete_A_User`, `Reject_Empty_And_Case_Insensitively_Duplicate_User_Names`, `ScimSurfaceShould.Serve_The_User_Operation_Matrix`, `ScimSurfaceShould.Convert_Unsupported_Media_Types_To_Scim_Errors` |
| `GET /scim/v2/Users`     | Query parameters per the pagination matrix  | 200     | `400 invalidFilter`, `401`, `403`                                                              | `ScimUsersShould.Page_List_Responses_With_Clamped_Start_Index_And_Count`, `ScimSurfaceShould.Ignore_Unsupported_Collection_Parameters_And_Reject_Filters` |
| `GET /scim/v2/Users/{id}` | none                                       | 200     | `404`, `401`, `403`                                                                            | `ScimUsersShould.Create_Get_List_Replace_And_Delete_A_User`                                    |
| `PUT /scim/v2/Users/{id}` | `userName` (required), `displayName`, `active`, `password` | 200 | `400 invalidValue`, `400 invalidSyntax`, `404`, `409 uniqueness`, `415 invalidSyntax`, `401`, `403` | `ScimUsersShould.Replace_Does_Not_Create_Missing_Users`, `Keep_The_Server_Id_Immutable_Across_Rename_And_Rotate_The_Security_Stamp`, `ScimSurfaceShould.Convert_Unsupported_Media_Types_To_Scim_Errors` |
| `DELETE /scim/v2/Users/{id}` | none                                    | 204     | `400 invalidValue`, `404`, `409 uniqueness`, `401`, `403`                                      | `ScimUsersShould.Create_Get_List_Replace_And_Delete_A_User`                                    |
| `PATCH /scim/v2/Users/{id}` | ignored                                  | 501     | `401`, `403`                                                                                   | `ScimUsersShould.Report_Patch_As_Not_Implemented`, `ScimSurfaceShould.Serve_The_User_Operation_Matrix` |

Read-only input (`id`, `schemas`, `meta`, `groups`, `externalId`, and attributes outside the writable
set) is ignored; the response never contains `password`
(`ScimUsersShould.Ignore_Read_Only_Input_And_Never_Return_The_Password`). `active` maps to the
administrative lockout sentinel (`Map_Active_To_The_Administrative_Sentinel`,
`Leave_A_Temporary_Lockout_Active_Until_Deprovisioning`).

## Groups

Groups are routed only when the host registers the default role services. A custom-role host omits
the Group resource type from discovery and removes the controller
(`ScimGroupsShould.Keep_Groups_Off_A_Custom_Role_Host`).

| Operation                 | Request attributes                       | Success | Errors                                                                                         | Evidence                                                                                     |
| ------------------------- | ---------------------------------------- | ------- | ---------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| `POST /scim/v2/Groups`    | `displayName` (required), `members[]`     | 201     | `400 invalidValue`, `400 invalidSyntax`, `403`, `409 uniqueness`, `415 invalidSyntax`, `401`    | `ScimGroupsShould.Create_Get_List_Replace_And_Delete_A_Group`, `Require_A_Display_Name`, `Validate_Member_Value_Reference_And_Type` |
| `GET /scim/v2/Groups`     | Query parameters per the pagination matrix | 200   | `400 invalidFilter`, `401`, `403`                                                              | `ScimGroupsShould.Page_List_Groups_With_Clamped_Start_Index_And_Count`, `Reject_Group_Filters_And_Report_Patch_As_Not_Implemented` |
| `GET /scim/v2/Groups/{id}` | none                                    | 200     | `404`, `401`, `403`                                                                            | `ScimGroupsShould.Create_Get_List_Replace_And_Delete_A_Group`                                  |
| `PUT /scim/v2/Groups/{id}` | `displayName` (required), `members[]`    | 200     | `400 invalidValue`, `400 invalidSyntax`, `403`, `404`, `409 uniqueness`, `415 invalidSyntax`    | `ScimGroupsShould.Replace_Clears_The_Complete_Membership`, `Reject_A_Duplicate_Display_Name_On_Replace` |
| `DELETE /scim/v2/Groups/{id}` | none                                  | 204     | `409`, `403`, `404`, `401`                                                                     | `ScimGroupsShould.Create_Get_List_Replace_And_Delete_A_Group`                                  |
| `PATCH /scim/v2/Groups/{id}` | ignored                                | 501     | `401`, `403`                                                                                   | `ScimGroupsShould.Reject_Group_Filters_And_Report_Patch_As_Not_Implemented`, `ScimSurfaceShould.Serve_The_Group_Operation_Matrix` |

`members[].value` is a user `Uid`; `members[].type` accepts `User`; `members[].$ref` must end with
`/Users/{value}`; `members[].display` is read-only and derived from the user. Nested groups, unknown
users, and a mismatched `$ref` return `400 invalidValue`
(`ScimGroupsShould.Validate_Member_Value_Reference_And_Type`). PUT replaces the complete managed
membership and clears every link when `members` is omitted or empty
(`ScimGroupsShould.Replace_Clears_The_Complete_Membership`); a failing membership advisor rolls back
the role and links (`Roll_Back_A_Failed_Membership_Replacement`).

The Group schema declares `displayName` uniqueness `server`, matching the `409 uniqueness` returned
for a duplicate normalized role name (`ScimGroupsShould.Reject_A_Duplicate_Display_Name_On_Replace`).

`User.groups` is read-only, lists managed memberships with `type: "direct"`, and is populated from
the group side (`ScimGroupsShould.Create_Get_List_Replace_And_Delete_A_Group`). `User.groups` is
`[]` when no managed membership exists.

## Method matrix

| Path                                | GET                  | POST                 | PUT                  | PATCH                | DELETE               |
| ----------------------------------- | -------------------- | -------------------- | -------------------- | -------------------- | -------------------- |
| `/scim/v2/ServiceProviderConfig`    | 200                  | 405                  | 405                  | 405                  | 405                  |
| `/scim/v2/ResourceTypes`            | 200                  | 405                  | 405                  | 405                  | 405                  |
| `/scim/v2/ResourceTypes/{id}`       | 200                  | 405                  | 405                  | 405                  | 405                  |
| `/scim/v2/Schemas`                  | 200                  | 405                  | 405                  | 405                  | 405                  |
| `/scim/v2/Schemas/{id}`             | 200                  | 405                  | 405                  | 405                  | 405                  |
| `/scim/v2/Users`                    | 200                  | 201                  | 405                  | 405                  | 405                  |
| `/scim/v2/Users/{id}`               | 200                  | 405                  | 200                  | 501                  | 204                  |
| `/scim/v2/Groups`                   | 200                  | 201                  | 405                  | 405                  | 405                  |
| `/scim/v2/Groups/{id}`              | 200                  | 405                  | 200                  | 501                  | 204                  |
| `/scim/v2/Bulk`                     | 404                  | 404                  | 404                  | 404                  | 404                  |
| `/scim/v2/Me`                       | 404                  | 404                  | 404                  | 404                  | 404                  |
| `/scim/v2/Applications`             | 404                  | 404                  | 404                  | 404                  | 404                  |
| `/scim/v2/Users/.search`            | 404                  | 405                  | 404                  | 501                  | 404                  |

The `404` and `405` cells are SCIM error bodies from `ScimErrorMiddleware` or the resource
controllers. The `/Users/.search` row follows from the single-segment `{id}` route: `.search` is
looked up as a user id. Users and Groups cells are verified by
`ScimSurfaceShould.Serve_The_User_Operation_Matrix`, `ScimSurfaceShould.Serve_The_Group_Operation_Matrix`,
and `ScimSurfaceShould.Expose_No_Endpoints_For_Unsupported_Operations`. Discovery `405` cells are
verified by `ScimErrorShould.Reject_Undeclared_Discovery_Methods`; the POST case and the `405` body
are verified by `ScimErrorShould.Omit_The_Keyword_When_No_Scim_Type_Applies`. The `/Bulk`, `/Me`,
`/Applications`, and `.search` cells beyond the exercised GET and POST samples are derived from the
absence of route templates and are not each exercised.

`ScimSurfaceShould.Require_Authentication_For_Resources_Only` records the auth split: discovery
returns `200` to an anonymous caller, while `/Users` and `/Groups` return `401` with a SCIM error.

## Pagination and query matrix

| Parameter                | Users and Groups lists                                                                                   | Evidence                                                                                   |
| ------------------------ | -------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| `startIndex`             | Defaults to 1; `< 1` becomes 1; echoed unchanged; beyond the total yields an empty page                  | `ScimUsersShould.Page_List_Responses_With_Clamped_Start_Index_And_Count`                    |
| `count`                  | Defaults to `DefaultPageSize` (100); capped at `MaxPageSize` (200); `< 0` becomes 0; `0` yields an empty page with `totalResults` | `ScimUsersShould.Page_List_Responses_With_Clamped_Start_Index_And_Count`, `Cap_The_Page_Size_With_The_Configured_Maximum` |
| `filter`                 | Any value returns `400 invalidFilter`                                                                    | `ScimUsersShould.Reject_A_Filter_Expression`, `ScimGroupsShould.Reject_Group_Filters_And_Report_Patch_As_Not_Implemented` |
| `sortBy`, `sortOrder`    | Ignored; the response remains a `ListResponse`                                                          | `ScimSurfaceShould.Ignore_Unsupported_Collection_Parameters_And_Reject_Filters`             |
| `attributes`, `excludedAttributes` | Ignored; the full default attribute set is returned                                             | `ScimSurfaceShould.Ignore_Unsupported_Collection_Parameters_And_Reject_Filters`             |
| Unknown parameter        | Ignored                                                                                                  | RFC 7644 §3.4.2; actions bind only the declared parameters                                  |

`ListResponse` fields: `schemas` (the ListResponse URI), `totalResults`, `startIndex`,
`itemsPerPage` (the number of returned resources), and `Resources` (always an array). Users order by
`Uid`; Groups order by role `Uid`.

## Header matrix

| Header             | When                                        | Value                                                                                     |
| ------------------ | ------------------------------------------- | ----------------------------------------------------------------------------------------- |
| `Content-Type`     | Every SCIM response                         | `application/scim+json`; `204` responses have no body                                      |
| `Location`         | `POST` create                               | Absolute location of the created resource                                                  |
| `Content-Location` | Single-resource `GET`, `POST`, `PUT`, and single discovery `GET` | Equals `meta.location`                                            |
| `ETag`             | Never                                       | Not emitted                                                                                |
| `If-Match`         | Any write                                   | Read by no action; the write proceeds (`ScimSurfaceShould.Emit_No_Entity_Tags_And_Ignore_If_Match`) |
| `Retry-After`      | Never on the SCIM path                      | The SCIM error body carries no retry metadata                                              |

## Status and error keyword matrix

| Status | `scimType`      | Source                                                                                   |
| ------ | --------------- | ---------------------------------------------------------------------------------------- |
| 400    | `invalidValue`  | `ScimUserAdapter` and `ScimGroupAdapter` validation failures                             |
| 400    | `invalidSyntax` | Request-body deserialization failure, or MVC model-state failure under `/scim/v2`        |
| 400    | `invalidFilter` | `ScimUsersController.List` and `ScimGroupsController.List` when `filter` is present       |
| 401    | absent          | `ScimUsersAuthorizationFilter` for an unauthenticated principal                           |
| 403    | absent          | `ScimUsersAuthorizationFilter` denial, a missing policy, or a forbidden mutation          |
| 404    | absent          | Unknown resource or discovery id, and unrouted SCIM paths                                 |
| 405    | absent          | Undeclared method on a matched route                                                      |
| 409    | `uniqueness`    | Duplicate user or role name, and repository add/update conflicts                          |
| 409    | absent          | Repository mutation abort                                                                 |
| 415    | `invalidSyntax` | A request body media type has no SCIM input formatter; converted before ProblemDetails     |
| 500    | absent          | Unhandled exception; generic detail unless a `SchemataException` supplies the message     |
| 501    | absent          | PATCH on a User or Group item                                                             |

Every error body contains `schemas: ["urn:ietf:params:scim:api:messages:2.0:Error"]`, a JSON string
`status`, and a `detail`. `scimType` appears only for the keywords above. The AIP-193 `error`
envelope never appears (`ScimErrorShould.Describe_Model_State_Failures_With_The_Invalid_Syntax_Keyword`,
`Omit_The_Keyword_When_No_Scim_Type_Applies`, `Map_Unhandled_And_Domain_Failures_To_Scim_Errors`). The
`415` body and media type are verified by
`ScimSurfaceShould.Convert_Unsupported_Media_Types_To_Scim_Errors`; non-SCIM paths keep the host's
error handling (`ScimErrorShould.Leave_Non_Scim_Paths_Out_Of_The_Protocol`).
