using Newtonsoft.Json;

namespace Steam
{
    public class UserAsset
    {
        /// <summary>Application (Game) ID</summary>
        public int AppId;

        /// <summary>Iventory Type</summary>
        public long ContextId;

        /// <summary>Item ID</summary>
        [JsonProperty("assetid")]
        public ulong Id { get; set; }

        public int Amount = 1;

        public override string ToString()
        {
            return string.Format("id:{0}, appid:{1}, contextid:{2}, amount:{3}",
            Id, AppId, ContextId, Amount);
        }

        public override int GetHashCode()
        {
            return string.Format("{0}-{1}-{2}-{3}",
            Id, AppId, ContextId, Amount).GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (!(obj is UserAsset))
                return false;

            UserAsset asset = (UserAsset)obj;

            return (
                this.Id == asset.Id &&
                this.AppId == asset.AppId &&
                this.ContextId == asset.ContextId &&
                this.Amount == asset.Amount
                );
        }
    }
}